# ContextEngine.Api

*[Slovenská verzia / Slovak version: [README.sk.md](README.sk.md)]*

A local ASP.NET Core Web API that turns Word/PDF documents into a searchable, structured knowledge base for AI agents. It parses documents into a tree of chunks (headings, paragraphs, tables), tags each chunk with topics/tags via Claude, embeds it locally for semantic search, and exposes it all over a small REST API.

## What it does

1. **Upload** a `.docx` or `.pdf` file (by local path).
2. The matching parser (`DocxParser` / `PdfParser`) splits it into **chunks** — headings, paragraphs, tables — preserving document order and structure.
3. Each document's chunks are sent to **Claude** (via the `Anthropic` SDK) to derive 1–5 document-level **topics** and 1–5 **tags** per chunk, reusing existing topic/tag values where a good fit exists.
4. Each chunk's text is embedded locally with the **all-MiniLM-L6-v2** sentence-embedding model, running entirely on-device via ONNX Runtime (no cloud embedding API, no key required for this step).
5. Chunks are stored in **SQLite** (via EF Core) and can be retrieved, filtered (by document, topic, tag, date range) or **semantically searched** by cosine similarity against the query's own embedding.

## Tech stack

- **.NET 8** / ASP.NET Core Web API
- **Entity Framework Core 8** + SQLite
- **ASP.NET Core Identity** (bearer tokens) for authentication/authorization
- **Microsoft.SemanticKernel.Connectors.Onnx** for local embeddings
- **Anthropic SDK** (Claude) for topic/tag generation
- **DocumentFormat.OpenXml** (.docx) and **PdfPig** (.pdf) for parsing
- **Swashbuckle** (Swagger/OpenAPI)
- **xUnit** for tests (unit + in-process API integration tests)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- The [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) tool, to apply database migrations:
  ```bash
  dotnet tool install --global dotnet-ef
  ```
- An Anthropic API key, set as the `ANTHROPIC_API_KEY` environment variable. Required for `POST /api/documents` (topic/tag generation); everything else works without it.

## Getting started

```bash
# 1. Restore & build
dotnet build ContextEngine.Api.sln

# 2. Create/update the local SQLite database (application data + Identity tables)
dotnet ef database update --project ContextEngine.Api --startup-project ContextEngine.Api

# 3. Set your Anthropic key (only needed for document upload)
setx ANTHROPIC_API_KEY "sk-ant-..."      # Windows, persists across sessions
# export ANTHROPIC_API_KEY="sk-ant-..." # macOS/Linux/WSL, current shell only

# 4. Run
dotnet run --project ContextEngine.Api
```

The API starts at `https://localhost:7056` (and `http://localhost:5209`) and opens Swagger UI at `/swagger` automatically in development.

> The database is not migrated automatically at startup — step 2 above must be rerun after pulling changes that add a new migration (check `ContextEngine.Api/Migrations/`).

## Authentication

Every endpoint under `/api/*` requires a bearer token. The token endpoints themselves (`/register`, `/login`, `/refresh`, ...) are provided out of the box by ASP.NET Core Identity's `MapIdentityApi` — there's no custom `AuthController` in the codebase.

```bash
# Register once
curl -X POST https://localhost:7056/register \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Some-Strong-Password1!"}'

# Log in to get a token
curl -X POST https://localhost:7056/login \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Some-Strong-Password1!"}'
# -> {"tokenType":"Bearer","accessToken":"...","expiresIn":3600,"refreshToken":"..."}

# Call a protected endpoint
curl https://localhost:7056/api/search \
  -H "Authorization: Bearer <accessToken>"
```

Access tokens expire after 1 hour (`expiresIn`); use `/refresh` with the `refreshToken` to get a new one without logging in again.

In Swagger UI, click **Authorize** and paste the token to have it attached to every request you send from there.

## API overview

| Area | Endpoint | Description |
|---|---|---|
| Documents | `POST /api/documents` | Parse and store a document by local file path |
| Documents | `GET /api/documents/{id}` | Get a document's chunks |
| Documents | `GET /api/documents/by-topic/{topic}`, `by-tag/{tag}`, `by-date-range` | Filter document ids |
| Documents | `DELETE /api/documents/{id}` | Delete a document and its chunks |
| Chunks | `GET /api/chunks/{id}` | Get a single chunk |
| Chunks | `GET /api/chunks/by-document/{id}`, `by-topic/{topic}`, `by-tag/{tag}`, `by-date-range` | Filter chunks |
| Chunks | `PUT /api/chunks/{id}` | Update a chunk |
| Chunks | `DELETE /api/chunks/{id}` | Delete a chunk (and its sub-tree) |
| Search | `POST /api/search` | Semantic search with optional type/topic/tag filters |
| Search | `GET /api/search` | List the topics/tags/types currently available to filter on |

Full request/response shapes are documented in Swagger UI (`/swagger`).

## Project structure

```
ContextEngine.Api/           API project
  Controllers/                REST endpoints (Documents, Chunks, Search)
  Services/                   Business logic (DocumentService, ChunkService, SearchService, AiHelper, OnnxEmbeddingService)
  Parsers/                    Docx/Pdf -> chunk extraction
  Models/                     EF entities (Chunk) and the Identity user (ApplicationUser)
  DTOs/, Mappings/            API-facing shapes and entity<->DTO conversion
  Migrations/                 EF Core migrations (application schema + Identity schema)
  EmbeddingModel/              Bundled local ONNX embedding model (see NOTICE.txt for license)
ContextEngine.Api.Tests/     xUnit tests (Unit/ and Api/ integration tests)
```

## Running tests

```bash
dotnet test ContextEngine.Api.sln
```

Integration tests (`ContextEngine.Api.Tests/Api/`) boot the API in-process against a temp SQLite database and bypass authentication by default (see `ContextEngineApiFactory`) so they can focus on business logic; `AuthenticationApiTests` specifically exercises the real register/login/`[Authorize]` flow.

## License notes

The bundled embedding model (`ContextEngine.Api/EmbeddingModel/model.onnx`, `vocab.txt`) is redistributed unmodified from [Xenova/all-MiniLM-L6-v2](https://huggingface.co/Xenova/all-MiniLM-L6-v2) under the Apache License 2.0 — see [`EmbeddingModel/NOTICE.txt`](ContextEngine.Api/EmbeddingModel/NOTICE.txt).
