---
name: mcp-builder
description: Node/TypeScript MCP SDK, Zod schemas, stdio transport, all 10 tool mappings, optimistic concurrency.
trigger: model_decision
---

# MCP Adapter Builder

## Setup

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const server = new McpServer({ name: "idempotency-engine", version: "1.0.0" });
const transport = new StdioServerTransport();
await server.connect(transport);
```

Use `stdio` transport for local agent integrations. MCP is **NOT a gateway** — it is a thin adapter over the C# Ingest API. All business logic stays in the C# API.

## The 10 MCP Tools

| Tool | HTTP Endpoint | Type |
|---|---|---|
| `lookup_error` | `GET /lookup` | Read-only |
| `search_memory` | `GET /search` | Read-only |
| `list_domains` | `GET /domains` | Read-only |
| `record_problem` | `POST /ingest/sync` (kind=problem) | Write |
| `record_decision` | `POST /ingest/sync` (kind=decision) | Write |
| `record_solution` | `POST /variants/:id/solutions` | Write |
| `report_solution_failure` | `POST /variants/:id/failures` | Write |
| `verify_solution` | `POST /variants/:id/verify` | Write |
| `link_anchors` | `POST /links` | Write |
| `confirm_link` | `PATCH /links/:id` (kind=confirmed) | Write |
| `reject_link` | `PATCH /links/:id` (kind=rejected) | Write |

## Tool Registration with Zod Validation

```typescript
server.tool(
  "lookup_error",
  "Look up known solutions for an error or problem",
  {
    text: z.string().describe("The error message or problem statement"),
    context: z.record(z.string()).optional().describe("Key-value context (repo, file, function, etc.)"),
    domain: z.string().optional(),
  },
  async ({ text, context, domain }) => {
    const resp = await apiClient.get("/lookup", { params: { text, context, domain } });
    return { content: [{ type: "text", text: JSON.stringify(resp.data) }] };
  }
);
```

## State the MCP Server Must Carry Between Calls

The agent workflow requires persisting these between tool calls:
- `variant_id`: returned by `record_problem` / `record_decision` / `lookup_error` — needed for `record_solution`, `report_solution_failure`, `verify_solution`
- `expected_solution_id`: the current solution's UUID — needed for `record_solution` to enable optimistic concurrency

MCP's only extra responsibility is carrying these IDs between calls.

## Optimistic Concurrency on record_solution

`record_solution` requires `expected_solution_id`. On HTTP 409:
1. Call `GET /variants/:id` to get the current `solution.id`
2. Retry `record_solution` with the updated `expected_solution_id`

```typescript
server.tool("record_solution", "...", {
  variant_id: z.string().uuid(),
  solution_text: z.string(),
  expected_solution_id: z.string().uuid().nullable()
    .describe("UUID of the solution being superseded, or null if there is none"),
}, async ({ variant_id, solution_text, expected_solution_id }) => {
  try {
    const resp = await apiClient.post(`/variants/${variant_id}/solutions`, {
      text: solution_text,
      expected_solution_id
    });
    return { content: [{ type: "text", text: JSON.stringify(resp.data) }] };
  } catch (e) {
    if (e.response?.status === 409) {
      // Re-fetch and surface the conflict to the agent
      return { content: [{ type: "text", text: "409: solution was updated by another writer. Re-fetch variant to get current expected_solution_id." }] };
    }
    throw e;
  }
});
```

## Error Translation

| HTTP Status | MCP Response |
|---|---|
| 409 Conflict | Return structured conflict message, prompt agent to re-fetch |
| 503 + Retry-After | Return retriable error, include retry delay |
| 4xx | Surface error detail from RFC 7807 body |