---
name: skill-mcp-patterns
description: RFC 7807 error translation, 409/503 retry patterns, caching strategy for MCP tools.
trigger: model_decision
---

# MCP Production Patterns

## RFC 7807 Error Translation

The C# API returns RFC 7807 Problem Details on errors. Translate these to meaningful MCP tool responses:

```typescript
function handleApiError(error: AxiosError): McpToolResult {
  const body = error.response?.data as ProblemDetails;
  const status = error.response?.status;

  if (status === 409) {
    return {
      content: [{
        type: "text",
        text: `Conflict: ${body?.detail ?? "concurrent modification detected"}. Re-fetch the resource and retry with an updated expected_id.`
      }],
      isError: true
    };
  }

  if (status === 503) {
    const retryAfter = error.response?.headers["retry-after"] ?? "1";
    return {
      content: [{
        type: "text",
        text: `Service temporarily unavailable (advisory lock contention). Retry after ${retryAfter}s.`
      }],
      isError: true
    };
  }

  return {
    content: [{ type: "text", text: `API error ${status}: ${body?.detail ?? error.message}` }],
    isError: true
  };
}
```

## 409 Conflict Pattern (record_solution)

1. Call `record_solution` with `expected_solution_id`
2. On 409: call `GET /variants/:id` to read `current_solution.id`
3. Retry with updated `expected_solution_id`
4. If 409 again: surface to agent — do not retry indefinitely

## 503 Retry Pattern (ingest paths)

The advisory lock budget is 2000ms. On 503:
- Respect `Retry-After: 1` header
- Retry once (the stable `event_id` makes it idempotent)
- Do not implement exponential backoff for 503 — it is a transient contention signal, not a system failure

## Caching Strategy

| Tool | Cache | TTL | Reason |
|---|---|---|---|
| `list_domains` | In-memory (server lifetime) | Forever | Domains rarely change; safe to cache |
| `lookup_error` | In-memory per (text, context hash) | 30 seconds | Repeated identical lookups from agents |
| `search_memory` | None | — | Search params vary too much |
| All write tools | None | — | Never cache write responses |

```typescript
const domainCache = new Map<string, Domain[]>();

server.tool("list_domains", "...", {}, async () => {
  if (!domainCache.has("domains")) {
    const resp = await apiClient.get("/domains");
    domainCache.set("domains", resp.data);
  }
  return { content: [{ type: "text", text: JSON.stringify(domainCache.get("domains")) }] };
});
```

## Rate Limiting Awareness

The linker caps at **100 ANN queries per domain per minute**. If an agent is calling `lookup_error` or `search_memory` in a tight loop:
- Add a small client-side delay between consecutive calls to the same domain
- Surface rate limit errors (429 if implemented) as a prompt to slow down