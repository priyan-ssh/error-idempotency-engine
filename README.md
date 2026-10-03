# The Idempotency Engine

A dev-time memory for errors, problems, and decisions.

## Tech Stack & Runtime Environment
- **API Backend:** C# .NET 10 Web API (`net10.0`)
- **Database & Vectors:** PostgreSQL 16 + `pgvector` HNSW indexes
- **Database Migrations:** DbUp (Plain `.sql` scripts)
- **Watcher Service:** Go
- **Agent Adapter:** Node.js / TypeScript MCP Server
