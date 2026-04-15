---
name: expertise-api-design
description: Core design decisions and architecture for the expertise-api project, distilled from the design document in GitHub issue #1
user-invocable: false
---

## Design Decisions

| Concern | Decision |
|---------|----------|
| Runtime | .NET 10 (LTS) |
| API framework | ASP.NET Core Minimal APIs |
| OpenAPI | Scalar (`Scalar.AspNetCore`) |
| Data access | Repository pattern — `IExpertiseRepository` |
| Database | PostgreSQL 17 (single backend, no MongoDB) |
| PostgreSQL image | `pgvector/pgvector:pg17` |
| Connection pooling | PgBouncer 1.21+ sidecar (`edoburu/pgbouncer`, transaction mode) |
| Vector search | pgvector extension — `vector(384)` column with HNSW index (`vector_cosine_ops`) |
| Keyword search | PostgreSQL stored generated `tsvector` column with GIN index |
| Embeddings | In-process ONNX via `Microsoft.SemanticKernel.Connectors.Onnx` |
| Embedding model | `bge-micro-v2` (22.9MB, 384-dim, bundled in Docker image) |
| Embedding abstraction | `IEmbeddingGenerator<string, Embedding<float>>` (Microsoft.Extensions.AI) |
| Embedding input | `EmbeddingService.BuildInputText(title, body)` — single source of truth |
| Auth — personal (current) | Static API key bearer token via `Auth:ApiKey` config |
| Auth — business (future) | Azure Entra ID — OIDC client_credentials grant (production hardening phase) |
| Tags storage | PostgreSQL `text[]` with GIN index (not JSONB — avoids EF Core 10 `Contains()` bug) |
| Deployment | k3s — personal and business clusters |
| Local dev | Docker Compose (not a deployment target) |
| Ingress | ingress-nginx |
| TLS | cert-manager + Route53 DNS-01 ACME |
| DNS | DDNS cron script (no external-dns controller) |
| Secrets | SOPS + age (no in-cluster controller) |
| Container registry | GitHub Container Registry (`ghcr.io`, private) |
| Manifests | Helm chart — shared templates, per-environment values |
| Backup | Daily `pg_dump` CronJob → AWS S3 + manual `scripts/backup.sh` wrapper |
| CLI | `reembed` — regenerate all embeddings for model migration |

## Data Model

```csharp
public class ExpertiseEntry
{
    public Guid Id { get; set; }
    public required string Domain { get; set; }     // "azure-devops", "iac", "shared"
    public List<string> Tags { get; set; } = [];     // PostgreSQL text[] with GIN index
    public required string Title { get; set; }
    public required string Body { get; set; }        // markdown
    public EntryType EntryType { get; set; }
    public Severity Severity { get; set; }
    public required string Source { get; set; }      // agent id, "human", pipeline name
    public string? SourceVersion { get; set; }       // e.g. "EF Core 10.0.1" — staleness signal
    public Vector? Embedding { get; set; }           // pgvector vector(384), nullable until embedded
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeprecatedAt { get; set; }      // soft delete
    public NpgsqlTsVector? SearchVector { get; set; } // stored generated tsvector column
}

public enum EntryType { IssueFix, Caveat, Requirement, Pattern }
public enum Severity  { Info, Warning, Critical }
```

Single-row `EmbeddingMetadata` table tracks model name, dimensions, and `LastReembedAt`.

## API Surface

| Method | Endpoint | Scope | Purpose | Optional params |
|--------|----------|-------|---------|-----------------|
| GET | `/expertise` | `expertise.read` | Filter by domain, tags, type, severity | `domain`, `tags` (comma-separated), `entryType`, `severity`, `includeDeprecated` |
| GET | `/expertise/{id}` | `expertise.read` | Single entry | |
| POST | `/expertise` | `expertise.write` | Create entry (generates embedding) | |
| PATCH | `/expertise/{id}` | `expertise.write` | Update entry (regenerates embedding if title/body changed) | |
| DELETE | `/expertise/{id}` | `expertise.write` | Soft delete (sets DeprecatedAt) | |
| GET | `/expertise/search?q=` | `expertise.read` | Keyword full-text search (tsvector) | `includeDeprecated` |
| GET | `/expertise/search/semantic?q=` | `expertise.read` | Semantic vector search (pgvector) | `limit` (1-100, default 10), `includeDeprecated` |
| GET | `/health` | none | Liveness probe | |
| GET | `/query` | none | Interactive browser UI for read-only API exploration | |

CLI: `dotnet run --project src/ExpertiseApi -- reembed [--batch-size 50]`

## Authentication

- **Current:** API key auth only. Custom `AuthenticationHandler<>` validating `Bearer` token against `Auth:ApiKey` config. All authenticated clients receive both `expertise.read` and `expertise.write` scopes.
- **Future (production hardening):** Azure Entra ID OIDC client_credentials flow with per-token scope differentiation via `Auth:Mode` config switch.
- Scopes enforced via ASP.NET Core authorization policies (`ReadAccess`, `WriteAccess`).
- Scope claim constants defined in `Auth/AuthConstants.cs`.

## Embedding Architecture

In-process ONNX using `BertOnnxTextEmbeddingGenerationService` behind `IEmbeddingGenerator<string, Embedding<float>>`. Registered with `AddBertOnnxEmbeddingGenerator`. Requires `#pragma warning disable SKEXP0070`. Model/vocab paths configurable via `Onnx:ModelPath` and `Onnx:VocabPath` config keys (default: `models/model.onnx`, `models/vocab.txt`). The abstraction allows future substitution with Ollama or Azure OpenAI without changing application code.

The embedding input text is constructed by `EmbeddingService.BuildInputText(title, body)` — this is the single source of truth for what text gets embedded, used by POST, PATCH, and reembed.

## Repository Structure

```
src/ExpertiseApi/
  Program.cs               # Entry point, service registration, middleware
  wwwroot/                 # Static files — query page UI
  Endpoints/               # Minimal API endpoint definitions
  Models/                  # ExpertiseEntry, EmbeddingMetadata, enums (EntryType, Severity)
  Data/                    # DbContext, IExpertiseRepository, ExpertiseRepository, DesignTimeDbContextFactory
  Migrations/              # EF Core migrations (InitialCreate, AddSearchVector)
  Services/                # EmbeddingService, DeduplicationService
  Auth/                    # ApiKeyAuthHandler, AuthExtensions, AuthConstants
  Cli/                     # ReembedCommand
  models/                  # ONNX model files (bge-micro-v2) — not committed, needed at runtime
helm/expertise-api/        # Helm chart (shared templates, generic values)
deploy/local/              # Docker Compose, .env.example, pgvector init script
scripts/                   # download-models.sh
```

## Known Gotchas

- **Npgsql:** Pin to 10.0.1+. Avoid JSONB `Contains()` (issue #3745).
- **PgBouncer + Npgsql:** Connection string must include `No Reset On Close=true`. PgBouncer 1.21+ required for `max_prepared_statements`.
- **PgBouncer transaction mode:** Advisory locks, LISTEN/NOTIFY, session-level SET, SQL-level PREPARE/EXECUTE do not work across transactions.
- **PgBouncer `auth_dbname`:** Required in PgBouncer 1.21+ when using `auth_query` mode.
- **`/dev/shm`:** Default 64MB too small for PostgreSQL containers. Mount emptyDir Memory volume (128Mi).
- **PV reclaim policy:** k3s local-path defaults to `Delete`. Patch to `Retain` immediately.
- **pgvector init:** Use `.sh` script in `/docker-entrypoint-initdb.d/`, not `.sql` (pgvector issue #355).
- **Scalar:** Avoid deprecated-endpoint transformers until Scalar issue #6020 is resolved.
- **SKEXP0070:** `BertOnnxTextEmbeddingGenerationService` requires `#pragma warning disable SKEXP0070`.
- **No PDB** with `minAvailable: 1` for single-replica PostgreSQL — blocks node drain.
- **SOPS key:** Back up age private key separately — if lost, encrypted secrets are unrecoverable.
- **k3s:** Must disable Traefik with `--disable=traefik` at install time.
- **`reembed` CLI:** Run as a one-off k8s Job, not from a running API replica, to avoid concurrent row writes.
- **Keyword search uses raw SQL:** The stored `SearchVector` column cannot be queried via LINQ — `KeywordSearchAsync` uses `FromSqlInterpolated`.
- **ONNX model files not committed:** `src/ExpertiseApi/models/` is gitignored. Model files must be present at runtime for embedding generation and semantic search. CRUD and keyword search work without them.
- **`EmbeddingMetadata` not auto-updated:** The metadata row is only written by the `reembed` CLI command, not by normal POST/PATCH operations.

## Implementation Status

All 6 personal phase steps are complete:

| Step | Status | What |
|------|--------|------|
| 1 | Done | Data model + EF Core migrations (`InitialCreate`, `AddSearchVector`) |
| 2 | Done | API endpoints (CRUD + keyword search) + API key auth |
| 3 | Done | Docker Compose local dev (postgres + pgbouncer + API) |
| 4 | Done | Embedding service (ONNX) + semantic search + `reembed` CLI |
| 5 | Done | Helm chart + SOPS secrets + bootstrap manifests + DDNS script |
| 6 | Done | Backup CronJob (Helm) + manual backup/restore wrapper |

## Production Hardening Phase (not started)

- Entra ID OIDC integration for business deployment
- Business k3s bootstrap and deploy
- Monitoring / observability (OpenTelemetry, Prometheus metrics)
- Rate limiting
- CI/CD pipeline

## Full Design Document

For the complete design including PostgreSQL tuning parameters, Helm values structure, Kubernetes deployment details, and cluster bootstrap steps, see GitHub issue #1.
