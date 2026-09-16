# The Idempotency Engine — Architecture Spec v1 (Final)

A dev-time memory for errors, problems, and decisions. Built around three resolution mechanisms: **collapse**, **discriminate**, **relate**, **version**.

---

## 0. What this system is

A local RAG that accumulates what you and your agents learn while developing. When an error appears, the system either returns a known solution or records a new entry for future lookup.

It is **not** observability. It does not alert, does not suppress, does not run on-call. It is a memory.

### The three cases it resolves

| Case | Description | Mechanism |
|---|---|---|
| **1 — Discriminate** | Same text, different cause → different fixes | Anchor groups by shape; variants discriminate by context |
| **2 — Relate** | Different text, same fix | Hashing collapses structurally similar; linking bridges distant wordings |
| **3b — Version** | Same text, same context, fix changes over time | Solutions are versioned; one current per variant |

### The three entry kinds

| Kind | What it is |
|---|---|
| `error` | A failure — log line, stack trace, command output |
| `problem` | A statement of what you're working on — "hibernation fails on wake" |
| `decision` | A recorded choice — "chose pipewire over pulseaudio" |

All three share the same schema, pipeline, and retrieval surface.

### Core design principles

- **One API, many clients.** The C# Ingest API is the source of truth. Watcher, MCP, CLI, and any future dashboard are clients. MCP is the agent adapter — not a gateway.
- **Idempotency is layered.** HTTP event IDs, unique constraints, advisory lock, optimistic checks, and peek-before-embed. Each layer catches what the previous missed.
- **Two axes:** `kind` and `domain`. Everything else is context or config.
- **Peek first, embed conditionally, lock only on insert.**
- **Lookup is a cascade:** exact → variant KNN → anchor ANN. Each level prunes the next.

---

## 1. Architecture

```
                  Clients
    ┌─────────────────────────────────────┐
    │ Watcher  MCP  CLI  Dashboard        │
    └────────────┬────────────────────────┘
                 │
                 ▼
    ┌─────────────────────────────────────┐
    │     Ingest API (C# .NET 9)          │
    │  /ingest  /ingest/sync  /lookup     │
    │  /search  /incidents  /variants     │
    │  /links   /domains    WS /events    │
    └────────────┬────────────────────────┘
                 │
                 ▼
    ┌─────────────────────────────────────┐
    │        Idempotency Engine           │
    │  ┌──────────────┐  ┌─────────────┐  │
    │  │ Idempotency  │  │   Linker    │  │
    │  │    Gate      │  │  + Sweep    │  │
    │  └──────┬───────┘  └─────────────┘  │
    │         ▼                            │
    │  ┌──────────────┐                    │
    │  │     Peek     │                    │
    │  └──────┬───────┘                    │
    │         ▼                            │
    │  ┌──────────────┐                    │
    │  │  Critical    │                    │
    │  │   Section    │                    │
    │  └──────────────┘                    │
    └────────────┬────────────────────────┘
                 │
                 ▼
    ┌─────────────────────────────────────┐
    │            Postgres                 │
    │  ProcessedIngests  Domains          │
    │  ProcessedIncidents                 │
    │  IncidentVariants  VariantSolutions │
    │  SolutionFailures  IncidentLinks    │
    │  SystemConfig                       │
    └─────────────────────────────────────┘
```

**No staging table. No async variant worker.** The watcher's batch is small. The ingest API processes items inline. Staging is deferred — added only if batch × latency exceeds the sync budget.

**MCP is not a gateway.** Manual entry, CLI, and any future dashboard call the C# API directly. MCP only adapts the API for AI agents.

---

## 2. The three cases, illustrated

### Case 1 — Same text, different cause

```
error: connection refused
   └── ANCHOR (kind=error, domain=postgres)
         ├── variant: service not started  → solution: systemctl start postgres
         ├── variant: firewall blocking    → solution: open port 5432
         └── variant: wrong port in config → solution: fix DB_PORT env
```

One anchor. Many variants. Each variant has its own solution.

### Case 2 — Different text, same fix

```
"Stored proc A not present"  ─┐
"Stored proc B not present"  ─┴─► ANCHOR A ("Stored proc <VAR> not present")
                                        │
                                        │ suggested link
                                        ▼
"Procedure Z does not exist" ────► ANCHOR B ("Procedure <VAR> does not exist")
```

Different anchors, connected by links. The linker finds them via embedding similarity.

**Case 2 has two paths:**

1. **At lookup time** — when the wording differs enough to produce a new shape_hash, the lookup cascade's anchor-level ANN surfaces the related anchor's solution immediately.
2. **At ingest time** — if the new wording is ingested, the linker creates a persistent `suggested` link between the two anchors.

Both paths use the same embedding threshold. Both are needed: lookup for immediate recall, links for persistent relationship tracking.

### Case 3b — Fix changes over time

```
VARIANT (hibernation fails on wake, kernel 6.8, nvidia)
   ├── Solution A  (superseded 2024-03, reason: kernel update)
   ├── Solution B  (superseded 2024-08, reason: nvidia driver fix)
   └── Solution C  (CURRENT)
```

One variant. Multiple solutions over time. The current one is served by default.

---

## 3. Data model

Eight tables. Four model the entities (anchor, variant, solution, link), two are metadata (domain, config), one gates ingestion, one logs failures.

### 3.1 Raw line to solution — the pipeline

```
Raw line          → "psql: connection to localhost:5432 failed: Connection refused"
                    (specific, one occurrence, discarded after processing)

Canonicalize      → "connection refused"
                    (rules strip variables)

ANCHOR            → sha256("connection refused")
                    (the shape, one row in ProcessedIncidents)

VARIANT           → context: {host: db-01, user: app}
                    (specific cause, one row in IncidentVariants)

SOLUTION          → "systemctl start postgres"
                    (the fix, one row in VariantSolutions)
```

### 3.2 The eight tables, explained

#### `ProcessedIncidents` — the anchor

**Entity:** a generalized error shape. `"connection refused"`, `"PM: hibernation failed: I/O error, dev <DEV>"`.

**Why it exists:** the collapse layer. Many raw lines that mean the same thing become one row. Identity is `(kind, domain, shape_hash, shape_version)`. Holds `error_embedding` for cross-anchor linking and lookup ANN, and `shape_template` for humans.

**Without it:** every raw line would be its own row. Retrieval returns hundreds of duplicates for the same issue.

#### `IncidentVariants` — the variant

**Entity:** a specific cause under an anchor.

**Why it exists:** the discriminate layer. Same error text, different root cause → different variants. Identity is `(incident_id, context_fingerprint)`. Holds `context_embedding` for semantic matching, `occurrence_count` for volume, `status` for review, `merged_by_embedding` for audit.

**Without it:** one anchor would carry one solution. You couldn't distinguish "connection refused because service is down" from "connection refused because firewall is blocking."

#### `VariantSolutions` — the solution

**Entity:** a fix. Versioned over time.

**Why it exists:** the versioning layer. One row per solution ever recorded; the current one is the row with `superseded_at IS NULL` (enforced by partial unique index). Holds `superseded_reason` and `verified`.

**Without it:** you'd overwrite solutions and lose history. No way to know the old fix stopped working or why.

#### `IncidentLinks` — the relation

**Entity:** "these anchors are probably the same issue."

**Why it exists:** the relate layer. Different wordings of the same underlying problem become different anchors. The link bridges them. `kind` is `suggested`, `confirmed`, or `rejected`. Auto-linking is within-kind only; cross-kind is explicit.

**Without it:** the persistent relationship graph would be missing. Lookup can still find related anchors via ANN, but you couldn't browse "all anchors related to this one."

#### `Domains` — namespace registry

**Entity:** a problem area. `hibernation`, `postgres`, `sound`, `hyprland`.

**Why it exists:** every anchor belongs to exactly one domain. Domain is part of the anchor's identity, so the same error text in two domains is two different anchors. Holds per-domain `shape_version`. Bump the rules, get new anchors without invalidating old ones.

**Without it:** no way to scope rules, rate limiting, or retrieval by problem area.

#### `SystemConfig` — global key-value

**Entity:** small bag of system-wide settings.

**Why it exists:** holds `active_embedding_model`, `lock_budget_ms`, `linker_last_checkpoint`. The checkpoint especially needs to be durable so a restart doesn't lose the sweep's place.

**Without it:** scattered config in environment variables or hardcoded constants.

#### `ProcessedIngests` — idempotency gate

**Entity:** "I have already processed this observation."

**Why it exists:** retries. If the watcher POSTs a batch, the API commits, and the response is lost, the watcher retries. Without this table, the retry double-counts `occurrence_count`. Every count increment is paired with an insert here in the same transaction.

**Not a lookup table.** `ProcessedIngests` is the retry gate for the ingest path only. Lookup ("have I seen this before?") queries `ProcessedIncidents` and `IncidentVariants`, which are never deleted. Pruning `ProcessedIngests` does not affect lookup, history, or retrieval.

**Retention:** rows older than 24 hours are deleted by a scheduled job. The retry window is minutes; 24h is generous margin.

#### `SolutionFailures` — failure log

**Entity:** "I tried this solution and it didn't work."

**Why it exists:** append-only. Records a specific observation about a specific solution on a specific variant. Does not automatically supersede the solution. Provides a trail for revising solutions later.

**Without it:** solutions with no track record.

### 3.3 What is an anchor?

An anchor **is** a generalized error message — but "generalized" is a specific, rule-driven process, not an intuition. An anchor is the message after the domain's rules have stripped its variables.

```
┌─ ANCHOR: "connection refused" ──────────────────────────┐
│ kind=error · domain=postgres · shape_hash=a3f... · v1   │
│                                                          │
│  ┌─ VARIANT ────────┐ ┌─ VARIANT ────────┐ ┌─ VARIANT ──┐│
│  │ {host:db-01}     │ │ {host:db-01,     │ │ {host:db-02││
│  │                  │ │  firewall:on}    │ │  port:5433}││
│  │ → systemctl      │ │ → ufw allow 5432 │ │ → fix env  ││
│  │   start postgres │ │                  │ │   DB_PORT  ││
│  └──────────────────┘ └──────────────────┘ └────────────┘│
└──────────────────────────────────────────────────────────┘
```

**What an anchor is not:**

- Not the raw error line (that's `raw_text`, never stored).
- Not the specific occurrence (that's a variant).
- Not a solution (solutions attach to variants).
- Not a link (that's a separate row connecting two anchors).

**Why "anchor":** because it's where things *anchor to*. Variants anchor to it. Links anchor to it. Embeddings anchor to it.

**Anchors can be errors, problems, or decisions.** The `kind` field distinguishes them. The generalization rules differ, but the shape of the entity is the same: a canonical string that things cluster around.

### 3.4 Relationships

```
Domains ──scopes──► ProcessedIncidents
                         │
                         ├──has many──► IncidentVariants
                         │                  │
                         │                  ├──has many──► VariantSolutions
                         │                  │                (versioned)
                         │                  │
                         │                  └──has many──► SolutionFailures
                         │
                         └──related to──► IncidentLinks

ProcessedIngests ──gates writes to──► IncidentVariants
SystemConfig     ──configures──►      ProcessedIncidents
```

**The three cases mapped to tables:**

| Case | Where it lives |
|---|---|
| 1 — Same text, different cause | `ProcessedIncidents` (group) + `IncidentVariants` (discriminate) |
| 2 — Different text, same fix | `IncidentLinks` (persistent) + lookup cascade's anchor ANN (immediate) |
| 3b — Fix changes over time | `VariantSolutions` (history) + partial unique index (one current) |

### 3.5 Schema

```sql
CREATE EXTENSION IF NOT EXISTS vector;

-- Idempotency gate: one row per processed observation
CREATE TABLE ProcessedIngests (
    event_id      TEXT PRIMARY KEY,
    processed_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_ingests_processed_at
    ON ProcessedIngests (processed_at);

-- Domains registry: holds per-domain shape_version
CREATE TABLE Domains (
    name           TEXT PRIMARY KEY,
    description    TEXT,
    shape_version  TEXT NOT NULL DEFAULT 'v1',
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Anchors: one per (kind, domain, shape_hash, shape_version)
CREATE TABLE ProcessedIncidents (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    kind              TEXT NOT NULL
        CHECK (kind IN ('error','problem','decision')),
    domain            TEXT NOT NULL REFERENCES Domains(name),
    shape_hash        TEXT NOT NULL,
    shape_version     TEXT NOT NULL,
    shape_template    TEXT NOT NULL,
    origin            TEXT NOT NULL
        CHECK (origin IN ('watcher','agent','human','dashboard')),
    error_embedding   VECTOR(384) NOT NULL,
    embedding_model   TEXT NOT NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (kind, domain, shape_hash, shape_version)
);

CREATE INDEX idx_incidents_kind_domain
    ON ProcessedIncidents (kind, domain);
CREATE INDEX idx_incidents_domain_created
    ON ProcessedIncidents (domain, created_at);
CREATE INDEX idx_incidents_embedding
    ON ProcessedIncidents
    USING hnsw (error_embedding vector_cosine_ops);

-- Variants: one row per (incident_id, context_fingerprint)
-- A variant is a SEMANTIC CAUSE GROUP.
-- Multiple fingerprints may map to one variant.
CREATE TABLE IncidentVariants (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    incident_id         UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    context_fingerprint TEXT NOT NULL,
    context_embedding   VECTOR(384) NOT NULL,
    embedding_model     TEXT NOT NULL,
    context_json        JSONB NOT NULL,
    context_sample      TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','needs_review')),
    verified            BOOLEAN NOT NULL DEFAULT false,
    merged_by_embedding BOOLEAN NOT NULL DEFAULT false,
    occurrence_count    BIGINT NOT NULL DEFAULT 1,
    version             BIGINT NOT NULL DEFAULT 1,
    first_seen_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (incident_id, context_fingerprint)
);

CREATE INDEX idx_variants_incident
    ON IncidentVariants (incident_id);
CREATE INDEX idx_variants_embedding
    ON IncidentVariants
    USING hnsw (context_embedding vector_cosine_ops);

-- Solutions: versioned. One current per variant.
CREATE TABLE VariantSolutions (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id         UUID NOT NULL REFERENCES IncidentVariants(id) ON DELETE CASCADE,
    text               TEXT NOT NULL,
    recorded_by        TEXT,
    recorded_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    superseded_at      TIMESTAMPTZ,
    superseded_reason  TEXT,
    verified           BOOLEAN NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX one_current_solution_per_variant
    ON VariantSolutions (variant_id)
    WHERE superseded_at IS NULL;

CREATE INDEX idx_solutions_variant
    ON VariantSolutions (variant_id);

-- Failures: append-only
CREATE TABLE SolutionFailures (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id    UUID NOT NULL REFERENCES IncidentVariants(id) ON DELETE CASCADE,
    solution_id   UUID REFERENCES VariantSolutions(id) ON DELETE SET NULL,
    note          TEXT,
    recorded_by   TEXT,
    recorded_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_failures_variant
    ON SolutionFailures (variant_id);

-- Links: related anchors, possibly cross-domain, possibly cross-kind
CREATE TABLE IncidentLinks (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    from_incident   UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    to_incident     UUID NOT NULL REFERENCES ProcessedIncidents(id) ON DELETE CASCADE,
    similarity      REAL NOT NULL,
    relation        TEXT NOT NULL DEFAULT 'related',
    kind            TEXT NOT NULL DEFAULT 'suggested'
        CHECK (kind IN ('suggested','confirmed','rejected')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (from_incident, to_incident)
);

CREATE INDEX idx_links_from
    ON IncidentLinks (from_incident) WHERE kind != 'rejected';
CREATE INDEX idx_links_to
    ON IncidentLinks (to_incident)   WHERE kind != 'rejected';

-- System config (generic key-value; not for per-domain data)
CREATE TABLE SystemConfig (
    key    TEXT PRIMARY KEY,
    value  TEXT NOT NULL
);
-- active_embedding_model
-- lock_budget_ms
-- linker_last_checkpoint
```

### 3.6 Key constraints at a glance

| Constraint | Enforces |
|---|---|
| `ProcessedIngests.event_id` PK | HTTP idempotency |
| `(kind, domain, shape_hash, shape_version)` UNIQUE | One anchor per shape |
| `(incident_id, context_fingerprint)` UNIQUE | One variant row per context |
| `one_current_solution_per_variant` partial UNIQUE | One non-superseded solution per variant |
| `(from_incident, to_incident)` UNIQUE | No duplicate directed links |

### 3.7 Semantics worth stating

- **`occurrence_count`** counts observations assigned to this semantic variant — not unique context fingerprints.
- **`version`** on `IncidentVariants` is for user-facing writes only. Ingest count increments do not bump it.
- **`origin`** is who produced the entry. Transport is request metadata, not stored.
- **`merged_by_embedding`** marks variants auto-folded from a fingerprint miss via KNN.
- **`ProcessedIngests` retention defines the idempotency window.** Retries within the window dedupe. Retries after the window are treated as new — correct, because no sane client retries 24h later.

---

## 4. Ingestion paths

| Path | Client | Endpoint | Batching | Returns |
|---|---|---|---|---|
| Watcher | Go binary | `POST /ingest` | 5s per-domain | `202 Accepted` |
| Agent | MCP → HTTP | `POST /ingest/sync` | single | Full variant state |
| Manual (CLI) | CLI → HTTP | `POST /ingest/sync` | single | Full variant state |
| Dashboard | React → HTTP | `POST /ingest/sync` | single | Full variant state |

All converge on the same critical section.

### 4.1 Watcher pipeline

```
journalctl -f
    │
    ▼
Reader loop
    │
    ▼
Match watch configs (per-domain filter)
    │
    ▼
Buffer per domain (5s window)
    │
    ▼
Canonicalize + shape_hash + context_fingerprint
    │
    ▼
Local dedup (collapse repeats within window)
    │
    ▼
Assign event_id per collapsed observation
    │
    ▼
POST /ingest (per-domain batch)
```

**No ring buffer. No LRU sieve. No exponential backoff.** The watcher only does: filter, canonicalize, hash, dedup, POST.

**Watcher does not embed.** It sends raw text + context + `rules_hash`. The API does all embedding.

### 4.2 `/ingest` payload

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

**Payload rules:**

- `samples` is an array. Each element is one collapsed observation.
- `event_id` is **required** and **stable**. Same observation retried → same `event_id`.
  - Watcher: derived from stable stream identity (systemd `__CURSOR` + sample index).
  - Agent / CLI / dashboard: caller-supplied UUID.
- `collapsed_count` is per-sample, minimum 1. Always present.
- `raw_text` and `context` are authoritative — the API recomputes hashes.
- `rules_hash` is diagnostic. The API's rules win.

### 4.3 Drift check

The watcher sends `rules_hash`. The API compares to its own for that domain. Mismatch → log warning, still process. The API's computation is authoritative.

### 4.4 Who does what — the full pipeline

Every component, every transformation, in order. This is the complete inventory of processing steps across the system.

```
═══════════════════════════════════════════════════════════════════
STAGE 1 — EDGE (Go Watcher)                                Client
═══════════════════════════════════════════════════════════════════

  1.  READ         journalctl -f → raw line
  2.  FILTER       raw line × watch configs → matched | dropped
                   └─ assigns domain from the matching config
  3.  BUFFER       matched lines → per-domain 5s window
  4.  CANONICALIZE raw text × domain rules → shape_string
  5.  HASH         shape_string → shape_hash = sha256
  6.  CONTEXT      fields → JSON → sorted keys → canonical string
  7.  FINGERPRINT  canonical context → sha256 → context_fingerprint
  8.  LOCAL DEDUP  group by (shape_hash, fingerprint) → collapse
                   └─ each group carries collapsed_count
  9.  ASSIGN ID    collapsed observation → event_id (stable)
 10.  POST         batch → POST /ingest

  Does NOT compute:   embeddings
  Does NOT know:      whether anchors exist

═══════════════════════════════════════════════════════════════════
STAGE 2 — API RECEIVE                                      Server
═══════════════════════════════════════════════════════════════════

 11.  RECEIVE      POST /ingest → batch
 12.  RE-CANON     raw text × in-memory rules → authoritative shape_hash
 13.  RE-CONTEXT   context fields → authoritative context_fingerprint
                   (Watcher's values are ignored; API's win.)

  Does NOT yet compute:  embeddings
  Does NOT yet access:   DB

═══════════════════════════════════════════════════════════════════
STAGE 3 — PEEK (read-only)                                 Server
═══════════════════════════════════════════════════════════════════

 14.  ANCHOR PEEK  SELECT anchor by (kind, domain, shape_hash, version)
 15.  VARIANT PEEK for each sample: SELECT variant by (anchor_id, fp)

  Purpose:  determine which case (A/B/C) we're in
  Writes:   none
  Reads:    two indexed lookups

═══════════════════════════════════════════════════════════════════
STAGE 4 — CONDITIONAL EMBEDDING                            Server
═══════════════════════════════════════════════════════════════════

 16.  SHAPE EMBED  if anchor missing: embed(shape_template)
 17.  CTX EMBED    for each sample whose variant is missing:
                   embed(context_canonical)

  Only embed what's needed.
    Case A: nothing
    Case B: contexts only
    Case C: shape + contexts

═══════════════════════════════════════════════════════════════════
STAGE 5 — CRITICAL SECTION                                 Server
═══════════════════════════════════════════════════════════════════

 18.  SORT         samples by context_fingerprint (deterministic)
 19.  BRANCH       Case A | B | C

  Case A: no lock, single txn per sample:
           idempotency INSERT + UPDATE count

  Case B: acquire_advisory_lock (budget 2000ms)
          per sample: idempotency INSERT
                     hits: UPDATE count
                     misses: recheck → KNN → insert

  Case C: acquire_advisory_lock
          INSERT anchor (ON CONFLICT RETURNING id)
          per sample: same as Case B
          if is_new: NOTIFY anchor_created

 20.  COMMIT       durability

  This is the ONLY write path in the system.

═══════════════════════════════════════════════════════════════════
STAGE 6 — POST-COMMIT (async)                              Linker
═══════════════════════════════════════════════════════════════════

 21.  NOTIFY RX    anchor_created event
                   OR: periodic sweep (5 min)
 22.  RATE GATE    count anchors per domain last minute
 23.  ANN          within kind, cross-domain allowed, same model
 24.  INSERT LINK  suggested links, ON CONFLICT DO NOTHING

  Never blocks ingest.
  Backed by periodic sweep for missed NOTIFYs.

═══════════════════════════════════════════════════════════════════
STAGE 7 — LOOKUP (read path, separate)                     Server
═══════════════════════════════════════════════════════════════════

 25.  RECEIVE      GET /lookup?text=...&context=...
 26.  CANON        text × rules → shape_hash
 27.  COMBINED     single query:
                   LEFT JOIN anchor + variant by (shape_hash, fp)
 28.  BRANCH       cascade (see §5.8):
                     fingerprint hit → return exact
                     anchor hit      → KNN over variants (embed context)
                     anchor miss     → ANN over anchors (embed text)

  Read-only. Never writes.

═══════════════════════════════════════════════════════════════════
```

### 4.5 Cost profile by path

| Path | DB queries | Model calls | Advisory lock | ANN |
|---|---|---|---|---|
| Ingest — Case A | 1 + N updates | 0 | No | No |
| Ingest — Case B | 1 + 1 + N | 1 + misses | Yes | No |
| Ingest — Case C | 1 + 1 + N | 2 + N | Yes | No |
| Linker fast path | 2 + 1 insert | 0 | No | 1 |
| Lookup — exact | 1 | 0 | No | No |
| Lookup — variant KNN | 2 | 1 | No | No |
| Lookup — anchor ANN | 2 | 1 | No | 1 |

---

## 5. The critical section

**Peek first, embed conditionally, lock only on insert. Idempotency-gate every write.**

### 5.1 The rule

- **Lock only when an INSERT might happen.**
- **Re-check only what you might INSERT.**
- **Updates to known rows are atomic — no lock, no re-check, no re-embed.**
- **Idempotency insert lives in the same transaction as the count increment it gates.**

### 5.2 The three paths

| Case | Anchor | Fingerprints | Lock? | Embed? | Idempotency |
|---|---|---|---|---|---|
| **A** | exists | all hit | No | No | Per-sample insert |
| **B** | exists | some miss | Yes | Contexts only | Inside lock |
| **C** | missing | — | Yes | Shape + contexts | Inside lock |

**Case A: ~5ms, no advisory lock, no embedding.**
**Case B: ~35ms, lock + one embedding.**
**Case C: ~70ms, lock + two embeddings.**

### 5.3 Pseudocode

```
-- 1. Canonicalize (in-memory rules, no DB, no lock)
shape_hash    = sha256(apply_rules(raw_text, domain_rules))
shape_version = SELECT shape_version FROM Domains WHERE name = $domain

-- 2. PEEK (read-only, no lock, no embedding)
anchor_id = SELECT id FROM ProcessedIncidents
            WHERE kind=$kind AND domain=$domain
              AND shape_hash=$shape_hash AND shape_version=$shape_version;

FOR EACH sample:
    IF anchor_id IS NOT NULL:
        sample.variant_id = SELECT id FROM IncidentVariants
                            WHERE incident_id=$anchor_id
                              AND context_fingerprint=sample.fingerprint;

-- 3. Deterministic batch ordering
samples = SORT(samples, BY context_fingerprint ASC)

-- 4. Branch

IF anchor_id IS NOT NULL AND all(variant_id IS NOT NULL):

    -- ═══ CASE A: FAST PATH ═══
    BEGIN TRANSACTION
    FOR EACH sample:
        claimed = INSERT INTO ProcessedIngests (event_id)
                  VALUES (sample.event_id)
                  ON CONFLICT (event_id) DO NOTHING
                  RETURNING event_id;

        IF claimed IS NOT NULL:
            UPDATE IncidentVariants
            SET occurrence_count = occurrence_count + sample.collapsed_count,
                last_seen_at = now()
            WHERE id = sample.variant_id;
    COMMIT
    RETURN samples

ELSIF anchor_id IS NOT NULL:

    -- ═══ CASE B: PARTIAL MISS ═══
    FOR EACH sample WHERE variant_id IS NULL:
        sample.vector = embed(sample.context_canonical);

    acquire_advisory_lock(); -- budget 2000ms, else 503

    FOR EACH sample:
        claimed = INSERT INTO ProcessedIngests (event_id)
                  ON CONFLICT (event_id) DO NOTHING RETURNING event_id;
        IF claimed IS NULL: CONTINUE;

        IF variant_id IS NOT NULL:
            UPDATE count += collapsed_count;
        ELSE:
            SELECT id INTO recheck WHERE context_fingerprint = sample.fingerprint;
            IF recheck IS NOT NULL:
                UPDATE count += collapsed_count;
            ELSE:
                -- Per-incident KNN. NOT through HNSW (see 5.6).
                SELECT id, (context_embedding <=> sample.vector) AS dist
                INTO match_id, dist
                FROM IncidentVariants
                WHERE incident_id = anchor_id
                  AND embedding_model = active_model
                ORDER BY context_embedding <=> sample.vector ASC
                LIMIT 1;

                IF match_id IS NULL OR dist >= 0.20:
                    INSERT variant status='active';
                ELSIF dist < 0.10:
                    UPDATE ... merged_by_embedding = true;
                ELSE:
                    INSERT variant status='needs_review';
    COMMIT

ELSE:

    -- ═══ CASE C: ANCHOR MISSING ═══
    error_vec = embed(shape_template);
    FOR EACH sample:
        sample.vector = embed(sample.context_canonical);

    acquire_advisory_lock();

    inserted_id = INSERT INTO ProcessedIncidents (...)
                  ON CONFLICT (kind, domain, shape_hash, shape_version)
                  DO NOTHING RETURNING id;

    IF inserted_id IS NOT NULL:
        anchor_id = inserted_id; is_new = true;
    ELSE:
        SELECT id INTO anchor_id FROM ProcessedIncidents WHERE ...;
        is_new = false;

    FOR EACH sample:
        claimed = INSERT INTO ProcessedIngests (event_id)
                  ON CONFLICT DO NOTHING RETURNING event_id;
        IF claimed IS NULL: CONTINUE;

        -- Same variant loop as Case B
        ...

    COMMIT
    IF is_new: NOTIFY anchor_created, anchor_id;
```

### 5.4 Thresholds (tunable)

`<=>` is cosine distance: `0 = identical`, `2 = opposite`. Initial values for `all-MiniLM-L6-v2`:

| Distance | Meaning | Action |
|---|---|---|
| `< 0.10` | candidate same variant | increment count, set `merged_by_embedding=true` |
| `0.10 ≤ d < 0.20` | borderline | insert `status='needs_review'` |
| `≥ 0.20` | novel cause | insert `status='active'` |

**These are classification heuristics, not correctness rules.** Fingerprint hits are authoritative. Embedding similarity produces *candidate* matches.

### 5.5 Why this is safe

- **Idempotency gate** — every count increment is paired with a `ProcessedIngests` insert in the same transaction.
- **Case A** issues only atomic `UPDATE count = count + N`. No advisory lock needed.
- **Cases B and C** hold the advisory lock only while doing the read-then-write variant loop.
- **Unique constraints** are the correctness backstop if the lock is ever bypassed.
- **Re-check under lock** handles races with pre-computed embeddings.
- **Deterministic batch order** guarantees identical input → identical grouping.
- **Cross-model suppression** in KNN ensures only same-model variants are compared.

### 5.6 Per-incident KNN does not route through HNSW

The candidate set under one anchor is small. The planner uses `idx_variants_incident` and does an exact sort. The HNSW index on `context_embedding` exists for future global queries but is not on this path.

### 5.7 Lock acquisition

```
deadline = now() + $lock_budget_ms       -- default 2000
acquired = false
WHILE now() < deadline AND NOT acquired:
    BEGIN TRANSACTION
    acquired = pg_try_advisory_xact_lock(
                   hashtextextended(
                       $kind||':'||$domain||':'||
                       $shape_hash||':'||$shape_version, 0))
    IF acquired: BREAK
    ROLLBACK
    IF now() >= deadline: BREAK
    sleep(50ms + jitter(0..25ms))
END
IF NOT acquired: RETURN 503 WITH Retry-After: 1
```

Uses `hashtextextended` (64-bit) not `hashtext` (32-bit).

### 5.8 The lookup cascade

Lookup is a read-only mirror of the ingest critical section. It resolves anchor and variant in one query, then fans out only as needed.

**The single query — resolves the first two guards in one round trip:**

```sql
SELECT a.id AS anchor_id, v.id AS variant_id
FROM ProcessedIncidents a
LEFT JOIN IncidentVariants v
  ON v.incident_id = a.id
 AND v.context_fingerprint = $fp
WHERE a.kind=$kind
  AND a.domain=$domain
  AND a.shape_hash=$shape_hash
  AND a.shape_version=$shape_version;
```

**The three-branch expression:**

```
match = fingerprint_hit ? exact_variant(row)
      : anchor_hit      ? knn_variant(row.anchor_id)
      :                   ann_anchor(embed(text))
```

- **Row has `variant_id`** → exact. Return solution. Zero model calls.
- **Row has `anchor_id`, no `variant_id`** → KNN over that anchor's variants. Embed context only.
- **Zero rows** → embed text, ANN over anchors (LIMIT 1). Return that anchor's top variant.

**Pruning.** Each guard prevents the next expensive operation:

| Guard | Prevents |
|---|---|
| `fingerprint_hit` | entire embedding path (model call + ANN) |
| `anchor_hit` | anchor-level embedding + ANN |
| no anchor | variant KNN (nothing to KNN against) |

**Response shape:**

```json
{
  "found": true,
  "anchor_id": "uuid",
  "variant_id": "uuid",
  "solution": "systemctl start postgres",
  "distance": 0.08,
  "verified": true
}
```

- `distance: null` → exact match.
- `distance: 0.0–0.20` → semantic match.
- `variant_id: null` → anchor known, cause not matched.
- `found: false` → nothing.

**One threshold: 0.20.** Ingest uses 0.10/0.20 because it's *deciding* (match / needs_review / new). Lookup uses 0.20 because it's *retrieving* (found / not found). Different operations, same cutoff number.

**Case 2 at lookup time.** When a query's wording differs enough to produce a new shape_hash, the anchor-level ANN surfaces the related anchor's solution immediately — not after the linker's next pass. This is what makes Case 2 work on the *first* lookup of a new wording.

**Query embedding cache.** Keyed on `sha256(text + active_model)`. LRU ~1000 entries. Repeat lookups of the same unfamiliar wording skip the model call.

---

## 6. Canonicalization and shape hashing

Per domain, a set of rules transforms the raw line into a shape string.

```yaml
rules:
  - pattern: "/dev/sd[a-z]"
    replace: "<DEV>"
  - pattern: "pid [0-9]+"
    replace: "pid <PID>"
  - pattern: "0x[0-9a-fA-F]+"
    replace: "<HEX>"
```

**Deterministic:**

```
shape_string = apply_rules(canonical_error_line)
             | canonical_error_line   -- if no rule matches
shape_hash   = sha256(shape_string)
```

Same input + same rules → same hash, always.

**`shape_version` is per-domain.** It lives on `Domains.shape_version`. Bumped when a domain's rule set changes.

**`rules_hash`** is the hash of the whole rule set for a domain. Diagnostic only.

**Dual-layer design.** Deterministic hashing collapses structurally similar text into anchors. Semantic linking bridges anchors that are textually distinct but conceptually related. Both layers are necessary.

---

## 7. Context (the variant key)

| Path | Fields (default) |
|---|---|
| Agent | `{repo, file, function, command, stack_summary}` |
| Manual / CLI | Same as agent |
| Watcher | `{unit, host, kernel, device, pid, cgroup}` |

**Canonical form:**

```
context_canonical   = JSON.stringify(context_json, keys_sorted)
context_fingerprint = sha256(context_canonical)
context_embedding   = embed(context_canonical)
```

Same canonical string for hashing (exact key) and embedding (semantic gate).

### Fingerprint vs. variant — the distinction

A **context fingerprint** is an exact identifier. A **variant** is a semantic cause group. Multiple fingerprints may map to one variant when the KNN gate finds them close enough. When that happens, `occurrence_count` absorbs the new count and `merged_by_embedding` is set for audit.

The schema's `UNIQUE (incident_id, context_fingerprint)` enforces one row per fingerprint, not one fingerprint per variant. The row is the observation's home; the variant is the semantic cluster.

---

## 8. Solution versioning (Case 3b)

One current solution per variant. History retained.

```
Solution A ──► Solution B ──► Solution C
(superseded)   (superseded)   (CURRENT)
```

### Recording a new solution (with optimistic concurrency)

```sql
BEGIN;

-- Conditional supersede. Only succeeds if the current solution is
-- still the one the caller saw.
UPDATE VariantSolutions
SET superseded_at = now(),
    superseded_reason = $reason
WHERE variant_id = $variant_id
  AND superseded_at IS NULL
  AND id = $expected_solution_id;

rows = ROW_COUNT;

-- expected_solution_id NULL means "I believe there is no current solution."
IF rows = 0 AND $expected_solution_id IS NOT NULL:
    ROLLBACK;
    RETURN 409 CONFLICT;                    -- caller re-fetches, retries

INSERT INTO VariantSolutions (variant_id, text, recorded_by, verified)
VALUES ($variant_id, $text, $recorded_by, false);

COMMIT;

-- If INSERT raises a unique violation on
-- one_current_solution_per_variant, another writer inserted between
-- our check and our insert. Translate to 409.
```

**No advisory lock.** The partial unique index and conditional supersede handle concurrency.

**Lookup** returns the current solution. History is a separate query.

**Failure recording** appends to `SolutionFailures`. Does not supersede automatically.

**Verification** flips `verified = true`. A new solution resets it to false.

---

## 9. Linking (Case 2)

Runs post-commit. Triggered by `NOTIFY anchor_created`. Backed up by a periodic sweep so missed notifications don't silently orphan anchors.

### 9.1 Fast path — NOTIFY

```
On notification(anchor_id):
    1. Fetch anchor: kind, domain, error_embedding, embedding_model
    2. Rate gate: COUNT(*) per domain in last minute. Over cap → drop.
    3. ANN within kind, cross-domain allowed, same embedding_model only.
       Neighbors with similarity > 0.90.
    4. INSERT IncidentLinks (suggested) ON CONFLICT DO NOTHING.
```

### 9.2 Backstop — periodic sweep

`LISTEN/NOTIFY` is at-most-once. The sweep runs every 5 minutes.

```sql
-- Read checkpoint
last = SELECT value FROM SystemConfig WHERE key = 'linker_last_checkpoint';

-- Find candidates
SELECT id, kind, domain, error_embedding, embedding_model
FROM ProcessedIncidents
WHERE created_at > $last
  AND embedding_model = $active_model
  AND NOT EXISTS (
      SELECT 1 FROM IncidentLinks WHERE from_incident = ProcessedIncidents.id
  )
ORDER BY created_at ASC;

-- For each: same rate gate + ANN + insert as fast path.

-- Advance checkpoint
UPDATE SystemConfig
SET value = now()::text
WHERE key = 'linker_last_checkpoint';
```

**No queue. No Kafka. No Redis.** One poll against a checkpoint.

### 9.3 Policy

- Auto-link **within kind**, cross-domain allowed.
- Auto-link **across kinds** requires explicit `POST /links`.
- Suggestions are `kind = 'suggested'`. Not authoritative.
- `PATCH /links/:id` flips to `confirmed` or `rejected`.
- **Cross-model suppression.** Only anchors with matching `embedding_model` are compared.

**Why cross-domain linking is required:** "postgres connection refused" (domain `postgres`) and "database connection refused" (domain `network`) are the same fix. Scoping linking to within-domain breaks Case 2.

### 9.4 Links vs. lookup ANN

Both find related anchors, but they serve different purposes:

| | Lookup ANN | Links |
|---|---|---|
| When | On demand | Persistent |
| Purpose | Find the answer to *this* query | Browse "anchors related to X" |
| Cost | One ANN query | Join or scan |
| Consumers | Agent | `/links`, `/incidents/:id`, UI |

Lookup does **not** traverse `IncidentLinks`. The ANN fallback in §5.8 covers the retrieval need directly. Links exist for browsing and review.

---

## 10. Config engine

```yaml
# config/watches/hibernation.yaml
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
context_fields: [unit, host, kernel, device]
batch_window_seconds: 5
```

**Watcher behavior:**

- Loads all configs at startup. Reloads on `SIGHUP`.
- Each config gets its own filter, rules, batch window, local dedup state.
- Emits entries with `domain` from config, `rules_hash` from its rule set.
- Assigns `event_id` per collapsed observation.
- `--dry-run` prints matches without POSTing.

**Domains** are registered in the `Domains` table, each carrying its own `shape_version`.

---

## 11. Embedding plugin

```
interface EmbeddingProvider {
    modelId(): string                 // "all-MiniLM-L6-v2"
    dimensions(): number              // 384
    embed(text: string): Promise<number[]>
    embedBatch(texts: string[]): Promise<number[][]>
}
```

**Default:** `all-MiniLM-L6-v2` via ONNX Runtime, 384 dims, CPU.

**Model stored on every anchor and every variant.** Vectors from different models are not comparable.

**Model change:** bump `active_embedding_model`, re-embed all anchors and variants in a maintenance pass.

**During a model swap:**

- New entries embed with the active model.
- KNN and linking only compare same-model vectors.
- Some existing variants are invisible to KNN until re-embedded. Safe default: better a duplicate than a misclassification.

### When embedding is called

| Situation | Shape embed | Context embed |
|---|---|---|
| Case A (fast path) | No | No |
| Case B | No | Fingerprint misses only |
| Case C | Yes | All samples |
| **Lookup, anchor missing** | **Yes** | No |
| Lookup, anchor found, variant missing | No | Yes |
| Lookup, exact variant hit | No | No |

---

## 12. API surface

### Ingest

```
POST   /ingest                   watcher batch (event_id per sample)
POST   /ingest/sync              single item (MCP, CLI, dashboard)
```

### Read

```
GET    /lookup                   lookup by text + context
GET    /search                   semantic + filtered search
GET    /incidents                list anchors
GET    /incidents/:id            anchor detail + variants
GET    /variants/:id             variant detail + solution history
GET    /variants/:id/solutions   full solution history
GET    /variants/:id/failures    failure log
GET    /links                    list links
GET    /domains                  list domains
```

### Write

```
POST   /variants/:id/solutions   record a solution (supersedes current)
POST   /variants/:id/failures    record a failure
POST   /variants/:id/verify      verify current solution
PATCH  /variants/:id             edit variant (expected_version required)
POST   /links                    explicit link
PATCH  /links/:id                confirm / reject
POST   /domains                  create domain
PATCH  /domains/:name            edit domain
```

### Events

```
WS     /events                   live broadcast (new_variant, resurgence)
```

### `/lookup` response

```json
{
  "found": true,
  "anchor_id": "uuid",
  "variant_id": "uuid",
  "solution": "systemctl start postgres",
  "distance": 0.08,
  "verified": true
}
```

| Field | Meaning |
|---|---|
| `found` | false if nothing matched |
| `anchor_id` | set if anchor matched (exact or via ANN) |
| `variant_id` | set if variant resolved; null if anchor known but no variant matched |
| `solution` | current solution text, if variant resolved and has one |
| `distance` | null for exact match; 0.0–0.20 for semantic |
| `verified` | true if the current solution was human-verified |

### Concurrency and error semantics

- `PATCH /variants/:id` requires `expected_version`. Mismatch → `409 Conflict`.
- Solution writes require `expected_solution_id`. Mismatch → `409 Conflict`.
- Lock contention on ingest returns `503 Service Unavailable` with `Retry-After: 1`.

---

## 13. MCP surface

MCP is a **subset** of the API, reshaped as agent tools. It is not a gateway.

| MCP tool | Maps to |
|---|---|
| `lookup_error` | `GET /lookup` |
| `search_memory` | `GET /search` |
| `record_problem` | `POST /ingest/sync` (kind=problem) |
| `record_decision` | `POST /ingest/sync` (kind=decision) |
| `record_solution` | `POST /variants/:id/solutions` |
| `report_solution_failure` | `POST /variants/:id/failures` |
| `verify_solution` | `POST /variants/:id/verify` |
| `link_anchors` | `POST /links` |
| `confirm_link` / `reject_link` | `PATCH /links/:id` |
| `list_domains` | `GET /domains` |

**MCP's only extra responsibility:** carrying `variant_id`, `expected_solution_id` between calls.

**Read-only:** `lookup_error`, `search_memory`, `list_domains`.

**`lookup_error` returns the unified response shape from §12.** Agents get `distance` and decide their own confidence threshold.

---

## 14. Invariants

1. **HTTP idempotency.** Every observation has a stable `event_id`. Count increments are paired with a `ProcessedIngests` insert in the same transaction.
2. **Determinism.** `shape_hash` is a pure function of `(shape_string, shape_version)`.
3. **Deterministic batch order.** Samples sorted by `context_fingerprint` before processing.
4. **Anchor uniqueness.** One anchor per `(kind, domain, shape_hash, shape_version)`.
5. **Variant uniqueness.** One row per `(incident_id, context_fingerprint)`.
6. **One current solution.** Partial unique index.
7. **Solution concurrency.** Conditional supersede + partial unique index. No lost updates.
8. **Bounded critical section.** Lock budget 2,000ms. Timeout → 503.
9. **Lock only on insert.** Fast path (Case A) takes no advisory lock.
10. **Single transaction.** Winning lock attempt carries through to COMMIT.
11. **Post-commit notify.** `NOTIFY anchor_created` only after COMMIT, only if new.
12. **Linker backstop.** Periodic sweep processes anchors the NOTIFY missed.
13. **Linking outside the lock.** Linker never blocks ingest.
14. **Embedding outside the lock.** All embedding before lock acquisition.
15. **Peek before embed.** Known errors pay no embedding cost.
16. **Cross-model suppression.** KNN and linking compare same-model vectors only.
17. **One API, many clients.** MCP is not in the path of any non-agent client.
18. **Embedding is a candidate signal.** Fingerprint hits are authoritative. Auto-merges are auditable.
19. **Lookup is a cascade with pruning.** Exact → variant KNN → anchor ANN. Each guard prevents the next expensive operation.
20. **One lookup threshold.** 0.20 for both variant KNN and anchor ANN. Different from ingest (which decides) because lookup only retrieves.

---

## 15. Chosen defaults

| # | Decision | Revisit if |
|---|---|---|
| 1 | Lock budget 2,000ms all paths | Contention observed |
| 2 | Retry interval 50ms + jitter 0–25ms | Latency noticeable under contention |
| 3 | Embedding: `all-MiniLM-L6-v2`, ONNX, local | Quality insufficient for problem statements |
| 4 | Lookup distance threshold: 0.20 for both variant KNN and anchor ANN | Calibration shows misclassification |
| 5 | Ingest distance thresholds: 0.10 / 0.20 | Calibration shows misclassification |
| 6 | Cross-kind linking: explicit only | Agents consistently want auto |
| 7 | Linking rate cap per domain: 100/min | Never hit at personal scale |
| 8 | Linker sweep interval: 5 minutes | Missed links observed for > 5min |
| 9 | Rules authored manually | Rule count > ~30 and maintenance hurts |
| 10 | Retention: nothing deleted except `ProcessedIngests` | DB > 1GB or query > 100ms |
| 11 | `ProcessedIngests` retention: 24 hours | Retries observed crossing the window |
| 12 | Cold-start: `lookup_error` returns `{ found: false }` | Never |
| 13 | Feedback: best-effort, not enforced | Accuracy degrades silently |
| 14 | Watcher batch: 5s per domain | Domain count × latency noticeable |
| 15 | Model change: re-embed all; suppress cross-model | Model changes frequently |
| 16 | Solution text: free-form markdown | Structured fields needed |
| 17 | Problems and errors share variant space | Divergent semantics |
| 18 | CLI is v1; dashboard deferred | Browser UI needed first |
| 19 | Peek-before-embed | Never — this is the point |
| 20 | `origin` replaces `source`; transport not stored | Transport tracking needed |
| 21 | Lookup query embedding cache: LRU ~1000 | Cache hit rate near zero |

---

## 16. Deferred

- **React dashboard.** Browse, confirm links, review `needs_review` variants.
- **Staging table + async variant worker.** Added if batch × latency exceeds the sync budget.
- **Hierarchical domains.** `hibernation/nvidia` etc.
- **Kind expansion beyond three.** Use tags, not new key parts.
- **Cross-version retrieval.** When `shape_version` bumps, all versions or only current?
- **Auto template mining.**
- **Auto cross-kind linking.**
- **Durable broadcast** beyond the periodic sweep.
- **Retention and archival** of old solutions (currently unbounded).
- **Domain renaming.** `Domains.name` is a namespace identifier; a rename requires aliasing.
- **Watcher restart loses in-flight batch.** Idempotency keys prevent double-counting if reused.
- **Mirrored `IncidentLinks`.** `A→B` and `B→A` can both exist as separate rows. Consumers must scan both directions.
- **HNSW + filter caveat.** If variants-per-anchor grows into thousands, revisit indexing strategy.
- **Reassignment audit trail.** Human corrections of `needs_review` aren't logged.
- **Multi-candidate lookup results.** Currently LIMIT 1 for the anchor ANN. Add LIMIT 5 with a candidate list if agents want to disambiguate.
- **Link traversal at lookup.** Not needed while the ANN fallback exists. Revisit if the link graph becomes the primary relationship model.
- **Distributed ingestion.** Multiple machines, queue-based. The critical section, idempotency gate, advisory lock, and NOTIFY work unchanged. Add `RawLines` table + drain worker.

---

## 17. What this system is not

- **Not observability.** No alerting, no on-call dashboards.
- **Not a log store.** Stores shapes, contexts, solutions — not raw log lines.
- **Not real-time.** 5s batch + async linker + periodic sweep. Lookups reflect last commit.
- **Not multi-user.** Shared knowledge is within one machine.
- **Not durable for broadcast.** `LISTEN/NOTIFY` is at-most-once; the sweep is the correctness backstop.
- **Not distributed.** One Postgres, one API, one watcher. Personal scale.

---

## 18. Implementation notes

### 18.1 Dockerization

**Docker for Postgres only. Native for the code you iterate on.**

```yaml
# docker-compose.yml
services:
  postgres:
    image: pgvector/pgvector:pg16
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: idempotency
      POSTGRES_USER: dev
      POSTGRES_PASSWORD: dev
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./migrations:/docker-entrypoint-initdb.d
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dev -d idempotency"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  pgdata:
```

Rationale:

- **Postgres** — you don't control it, `pgvector` needs version matching, reproducibility matters → Docker.
- **API, watcher, MCP** — you iterate on them constantly. Native `dotnet run` / `go run` / `node` are instant.

Add Dockerfiles for the services later, when you deploy or share.

### 18.2 ORM vs. raw SQL

**Use EF Core for reads. Use raw SQL for the critical section and lookup cascade. Never use EF migrations.**

| Layer | Tool | Reason |
|---|---|---|
| Schema | DbUp or plain `.sql` files | Full control; no generator fights |
| Domain model | C# records | Plain data types |
| Read paths (browse/search) | EF Core (no-tracking) | LINQ convenient for filters |
| Critical section | Dapper or `NpgsqlCommand` | Explicit SQL, no tracking |
| Lookup cascade | Dapper or raw SQL | `<=>`, `LEFT JOIN` guard, conditional ANN |
| Vector queries | Raw SQL | `<=>` is not a LINQ thing |
| Advisory locks | Raw SQL | Not a LINQ thing |

### 18.3 Migrations

Use DbUp with plain `.sql` files:

```
/migrations
  001_create_extensions.sql
  002_create_domains.sql
  003_create_incidents.sql
  004_create_variants.sql
  005_create_solutions.sql
  006_create_links.sql
  007_create_ingests.sql
  008_create_indexes.sql
```

### 18.4 Retention job

```sql
DELETE FROM ProcessedIngests
WHERE processed_at < now() - interval '24 hours';
```

Run hourly via `pg_cron`, a systemd timer, or a background task.

### 18.5 Suggested project structure

```
src/
  IdempotencyEngine.Domain/
    Models/                      ← C# records
    Interfaces/

  IdempotencyEngine.Data/
    Migrations/                  ← DbUp SQL files
    DbContext.cs                 ← EF Core, read-only use
    ReadRepositories/            ← EF Core reads
    WriteRepositories/           ← Dapper + raw SQL

  IdempotencyEngine.Api/
    Controllers/
    Ingestion/                   ← critical section
    Lookup/                      ← lookup cascade

  IdempotencyEngine.Watcher/     ← Go, per-endpoint

  IdempotencyEngine.Mcp/         ← Node.js MCP adapter

  IdempotencyEngine.Cli/         ← thin HTTP wrapper
```

---

## Summary

**Four mechanisms:**

1. **Collapse** — shape hashing collapses structurally similar text into anchors.
2. **Discriminate** — context fingerprint + embedding gate separate variants under an anchor.
3. **Relate** — ANN linking bridges anchors that differ textually but share fixes.
4. **Version** — `VariantSolutions` with one-current-per-variant, conditional supersede.

**Two axes:** `kind` and `domain`. Everything else is context or config.

**Eight tables:** four model entities (anchor, variant, solution, link), two are metadata (domain, config), one gates ingestion, one logs failures.

**Ingest:** peek first, embed conditionally, lock only on insert, idempotency-gate every write.

**Lookup:** a three-branch cascade — exact → variant KNN → anchor ANN. One query for the first two guards, one threshold (0.20), one response shape. Each guard prunes the next expensive operation.

**Idempotency is layered:**

- HTTP `event_id` in `ProcessedIngests` — retries don't double-count.
- Unique constraints — correctness for anchors, variants, solutions, links.
- Advisory lock — clean serialization of the variant loop only.
- Optimistic `expected_version` / `expected_solution_id` — versioned writes without locks.
- Peek-before-embed — known errors pay no embedding cost.

**One API, many clients.** The C# API is the source of truth. Watcher, MCP, CLI, and any future dashboard call it directly. MCP is the agent adapter — not a gateway.

**Linker is best-effort with a sweep backstop.** No queue, no broker, one poll against a checkpoint. Links are for browsing; lookup uses ANN directly.

**Docker for Postgres only.** Native for the code. DbUp for migrations, Dapper for the critical section and lookup cascade, EF Core optional for browse/search reads.

Time to build.