---
name: go-journalctl-watcher
description: Tailing journalctl -f, config loading, SIGHUP reload, 5s dedup window, event_id format, batch POST payload.
---

# Go Journalctl Watcher Pipeline

## Tailing journalctl

```go
cmd := exec.Command("journalctl", "-f", "--output=json")
stdout, _ := cmd.StdoutPipe()
cmd.Start()
scanner := bufio.NewScanner(stdout)
for scanner.Scan() {
    line := scanner.Text()
    // parse JSON, extract MESSAGE and SYSLOG_IDENTIFIER etc.
}
```

## Config File Format

One file per domain, in `config/watches/<domain>.yaml`:

```yaml
id: hibernation-fedora
domain: hibernation
stream:
  type: journalctl
  unit: "*"
filter: ".*(hibernat|suspend|resume|sleep).*"
rules:
  - pattern: "/dev/sd[a-z]"
    replace: "<DEV>"
  - pattern: "pid [0-9]+"
    replace: "pid <PID>"
  - pattern: "0x[0-9a-fA-F]+"
    replace: "<HEX>"
context_fields: [unit, host, kernel, device]
batch_window_seconds: 5
```

- Load all configs at startup.
- **Reload on `SIGHUP`** — do not restart the process.
- `--dry-run` flag: print matched + canonicalized lines without POSTing.

## Pipeline Stages (Buffered Channels)

```
journalctl stdout
    │
    ▼ [reader goroutine]
Match domain filters (per-config regex)
    │
    ▼ [canonicalize]
Apply rules → shape_string → shape_hash = sha256(shape_string)
Context JSON (sorted keys) → context_fingerprint = sha256(context_canonical)
    │
    ▼ [dedup — 5s window per domain]
Collapse identical (shape_hash, context_fingerprint) pairs
Accumulate collapsed_count (minimum 1)
    │
    ▼ [batch collector — fires every batch_window_seconds]
Assign event_id per collapsed observation
    │
    ▼ [POST goroutine]
POST /ingest (per-domain batch)
```

Use bounded buffered channels between stages. On channel full, **drop and log** — do not block the journalctl reader.

## event_id Format

Stable and replayable — derived from the journalctl stream cursor:

```
systemd:cursor=<__CURSOR field from journald JSON>:idx=<0-based index within batch>
```

Example: `systemd:cursor=s=abc...xyz:idx=0`

The same observation retried produces the same `event_id` → safe retry, idempotency gate prevents double-counting.

## Dedup Window

Per domain, maintain a map of `(shape_hash, context_fingerprint) → {collapsed_count, raw_text, context}`. Every 5 seconds (or `batch_window_seconds`), flush the map into a batch and reset it.

## Drift Check

Send `rules_hash` in every payload:

```go
rulesHash := sha256(serializeRules(domainConfig.Rules))
```

The API compares it against its own computation. A mismatch triggers a warning log on the API side — the watcher still processes normally. The API's canonicalization is always authoritative.

## POST /ingest Payload

```json
{
  "kind": "error",
  "domain": "hibernation",
  "origin": "watcher",
  "rules_hash": "b7c1e2...",
  "shape_version": "v1",
  "samples": [
    {
      "event_id": "systemd:cursor=abc123:idx=0",
      "raw_text": "PM: hibernation failed: I/O error, dev sda, sector 12345",
      "context": {
        "unit": "kernel",
        "host": "laptop",
        "kernel": "6.8.5",
        "device": "sda"
      },
      "collapsed_count": 47
    }
  ]
}
```

Context fields for watcher: `unit`, `host`, `kernel`, `device`, `pid`, `cgroup`.

## 503 Retry

On HTTP 503 (advisory lock timeout), retry with exponential backoff. The stable `event_id` guarantees the retry is idempotent — the API's `ProcessedIngests` gate prevents double-counting.

## The Watcher Does NOT Embed

Send `raw_text` + `context` + `rules_hash`. The C# API performs all canonicalization and embedding. The watcher's job is filter, canonicalize hashes, dedup, and POST.