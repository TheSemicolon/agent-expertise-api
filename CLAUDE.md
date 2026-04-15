# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Self-hosted .NET 10 REST API for storing and serving expertise entries consumed by AI agents (GitHub Copilot, Claude). Entries are a running log of issues/fixes, workarounds, caveats, and requirements — either domain-specific or shared across agent domains.

## Tech Stack

- **.NET 10** (LTS) with **ASP.NET Core Minimal APIs**
- **PostgreSQL 17** with **pgvector** extension for semantic search
- **EF Core** for data access (repository pattern via `IExpertiseRepository`)
- **PgBouncer 1.21+** sidecar for connection pooling (transaction mode)
- **In-process ONNX** embeddings via `Microsoft.SemanticKernel.Connectors.Onnx` (bge-micro-v2, 384-dim)
- **OpenAPI** docs via Scalar (`Scalar.AspNetCore`)
- **Docker Compose** for local dev; **Helm** chart for k8s deployment

## Prerequisites

```bash
# .NET 10 SDK (verify with: dotnet --version)
# Docker + Docker Compose
# EF Core CLI tool
dotnet tool install --global dotnet-ef
```

## Build & Run Commands

```bash
# Build
dotnet build src/ExpertiseApi/ExpertiseApi.csproj

# Run locally (requires PostgreSQL via Docker Compose)
dotnet run --project src/ExpertiseApi/ExpertiseApi.csproj

# EF Core migrations
dotnet ef migrations add <MigrationName> --project src/ExpertiseApi
dotnet ef database update --project src/ExpertiseApi

# Docker Compose local dev stack (database only)
docker compose -f deploy/local/docker-compose.yml up postgres pgbouncer

# Docker Compose full stack (database + API)
docker compose -f deploy/local/docker-compose.yml up

# Regenerate all embeddings (CLI command)
dotnet run --project src/ExpertiseApi -- reembed [--batch-size 50]
```

## Model Download

The ONNX model files are not tracked in git. Download them before running locally or building Docker images:

```bash
# Download bge-micro-v2 quantized model (~17.4 MB) and vocab.txt
./scripts/download-models.sh

# Force re-download (e.g., after model version bump)
FORCE=1 ./scripts/download-models.sh
```

Files land in `src/ExpertiseApi/models/` (gitignored). In CI, this step is cached and only runs when `scripts/download-models.sh` changes.

## Local Development Quick Start

```bash
# 1. Start the database layer
cp deploy/local/.env.example deploy/local/.env
# Edit deploy/local/.env — set POSTGRES_PASSWORD and AUTH__APIKEY
docker compose -f deploy/local/docker-compose.yml up -d postgres pgbouncer

# 2. Apply EF Core migrations
dotnet ef database update --project src/ExpertiseApi

# 2b. Download ONNX model files (required for embeddings and semantic search)
./scripts/download-models.sh

# 3. Run the API
dotnet run --project src/ExpertiseApi

# 4. Verify — health check (no auth required)
curl http://localhost:5000/health

# 5. Create an entry (requires API key from .env AUTH__APIKEY)
curl -X POST http://localhost:5000/expertise \
  -H "Authorization: Bearer dev-api-key-change-me" \
  -H "Content-Type: application/json" \
  -d '{
    "domain": "shared",
    "title": "Example expertise entry",
    "body": "This is a test entry for local development.",
    "entryType": "Pattern",
    "severity": "Info",
    "source": "human"
  }'

# 6. List entries
curl http://localhost:5000/expertise \
  -H "Authorization: Bearer dev-api-key-change-me"

# 7. Keyword search
curl "http://localhost:5000/expertise/search?q=test" \
  -H "Authorization: Bearer dev-api-key-change-me"

# 8. Semantic search (requires ONNX model files in src/ExpertiseApi/models/)
curl "http://localhost:5000/expertise/search/semantic?q=test&limit=5" \
  -H "Authorization: Bearer dev-api-key-change-me"

# 9. OpenAPI docs
# Browse to http://localhost:5000/scalar/v1

# 10. Query page (interactive browser UI for read-only browsing and search)
# Browse to http://localhost:5000/query
```

**Note:** Semantic search and embedding generation require the bge-micro-v2 ONNX model files (`model.onnx` and `vocab.txt`) in `src/ExpertiseApi/models/`. Without them, the API will start but POST/PATCH and semantic search will fail. CRUD and keyword search work without the model.

## Agent Integration

AI agents (Claude Code, GitHub Copilot) consume this API via HTTP with a bearer token. Typical agent workflow:

1. **Search** existing expertise before solving a problem: `GET /expertise/search?q=<query>` or `GET /expertise/search/semantic?q=<query>`
2. **Create** a new entry when discovering a fix, caveat, or pattern: `POST /expertise`
3. **Update** an entry when information changes: `PATCH /expertise/{id}`

All endpoints except `/health` require `Authorization: Bearer <api-key>`.

## CI/CD

| Workflow | Trigger | What it does |
| -------- | ------- | ------------ |
| `ci.yml` | PR to main, push to dev | `dotnet build` + `dotnet test` |
| `release.yml` | Push to main | Download models (cached), Docker build linux/amd64+arm64, push to GHCR |

GHCR image: `ghcr.io/thesemicolon/agent-expertise-api` (multi-arch: amd64 + arm64).

## Architecture & Design

For data model, API surface, authentication, embedding architecture, and known gotchas, see `.claude/skills/expertise-api-design/SKILL.md` (authoritative reference). Use the `expertise-api-owner` agent for design and implementation questions.
