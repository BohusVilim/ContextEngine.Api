# ContextEngine.Api

*[Slovenská verzia / Slovak version: [README.sk.md](README.sk.md)]*

A local ASP.NET Core Web API that turns Word/PDF documents into a searchable, structured knowledge base for AI agents. It parses documents into a tree of chunks (headings, paragraphs, tables), enriches each chunk with AI-generated topics/tags, embeds it locally for semantic search, and exposes it all over a small, token-authenticated REST API.

It is designed to run as a **local, single-tenant backend** — e.g. something an AI coding agent or a personal RAG tool talks to on `localhost` — rather than a public multi-tenant service. Several design choices below (bearer-token auth with no roles, file-path-based upload, in-memory filtering) reflect that.

## Table of contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Data model](#data-model)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Authentication & authorization](#authentication--authorization)
- [API reference](#api-reference)
- [How document parsing works](#how-document-parsing-works)
- [How AI enrichment works](#how-ai-enrichment-works)
- [How semantic search works](#how-semantic-search-works)
- [Project structure](#project-structure)
- [Testing](#testing)
- [Known limitations](#known-limitations)
- [License notes](#license-notes)

## Overview

The core idea: point the API at a `.docx` or `.pdf` file, and it becomes queryable knowledge.

```
 .docx / .pdf file
        │
        ▼
 ┌─────────────────┐   flat list of chunks (headings, paragraphs, tables),
 │ DocxParser /     │   in document order, with structural Type + Order
 │ PdfParser        │──────────────────────────────────────────────────┐
 └─────────────────┘                                                   │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ AiHelper (Claude)     │  document-level topics
                                                          │ CreateTopicsAsync /   │  + per-chunk tags
                                                          │ CreateTagsAsync       │
                                                          └──────────────────────┘
                                                                        │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ OnnxEmbeddingService  │  384-dim embedding
                                                          │ (local, no API call)  │  per chunk
                                                          └──────────────────────┘
                                                                        │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ SQLite (via EF Core)  │  persisted Chunk rows
                                                          └──────────────────────┘
                                                                        │
                                    GET /api/chunks/*, /api/documents/* │  POST /api/search
                                    (retrieval & filtering)             │  (cosine-similarity ranking)
                                                                        ▼
                                                                 AI agent / client
```

All of this happens synchronously inside a single `POST /api/documents` call — by the time it returns, the document is fully parsed, tagged, embedded and persisted.

## Architecture

The codebase follows a conventional layered structure with everything wired through dependency injection in [`Program.cs`](ContextEngine.Api/Program.cs):

| Layer | Namespace | Responsibility |
|---|---|---|
| **Controllers** | `ContextEngine.Api.Controllers` | Thin HTTP adapters — parse route/query/body, call one service method, map the result to a status code. No business logic. |
| **Services** | `ContextEngine.Api.Services` | Business logic: `DocumentService` (upload/parse/persist pipeline), `ChunkService` (CRUD/filtering on individual chunks), `SearchService` (filter + rank), `AiHelper` (Claude calls), `OnnxEmbeddingService` (local embeddings). |
| **Parsers** | `ContextEngine.Api.Parsers` | Format-specific extraction: `DocxParser` (Open XML SDK), `PdfParser` (PdfPig + heuristics). Each implements a narrow `IDocxParser`/`IPdfParser` interface so `DocumentService` doesn't need to know format details. |
| **Data / Models** | `ContextEngine.Api.Data`, `ContextEngine.Api.Models` | `ContextEngineDbContext` (EF Core, extends `IdentityDbContext<ApplicationUser>`) and the `Chunk` entity. |
| **DTOs / Mappings** | `ContextEngine.Api.DTOs`, `ContextEngine.Api.Mappings` | `ChunkDto` (read), `CreateChunkDto` (parser output, pre-persistence), and `ChunkMappings` converting between entity and DTO shapes. |

Every service is registered `Scoped` (one instance per HTTP request) except `IEmbeddingService`, which is `Singleton` — the ONNX model and its `InferenceSession` are loaded once at startup (via `AddBertOnnxEmbeddingGenerator` in `Program.cs`) and reused for the app's lifetime, since loading it per-request would be wasteful and the model itself is stateless/thread-safe for inference.

Cross-cutting concerns:
- **Errors**: `GlobalExceptionHandler` ([`GlobalExceptionHandler.cs`](ContextEngine.Api/GlobalExceptionHandler.cs)) is the single place unhandled exceptions get mapped to an HTTP status + [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `ProblemDetails` body. `NotSupportedException` → 400, `NotImplementedException` → 501, `FileNotFoundException`/`DirectoryNotFoundException` → 404, everything else → 500 with no message leaked to the client (the real exception is still logged server-side).
- **Enums over the wire**: controllers serialize enums (e.g. `ChunkType`) as their string name, not the underlying integer, via a `JsonStringEnumConverter` registered in `AddControllers().AddJsonOptions(...)` — so API payloads stay self-describing for an AI agent reading them without a schema.
- **Auth**: `[Authorize]` on every controller, backed by ASP.NET Core Identity bearer tokens — see [Authentication & authorization](#authentication--authorization).

## Data model

There's a single core entity, `Chunk` ([`Models/Chunk/Chunk.cs`](ContextEngine.Api/Models/Chunk/Chunk.cs)):

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `SourceId` | `Guid` | Id of the document this chunk belongs to. Every chunk parsed from one `POST /api/documents` call shares the same `SourceId` — there's no separate `Document` table. |
| `ParentId` / `Parent` / `Children` | `Guid?` / `Chunk?` / `List<Chunk>` | Self-referencing tree. Both parsers nest each chunk under the most recent heading open at that point (a heading itself nests under the most recent heading of a strictly outer level) — see [How document parsing works](#how-document-parsing-works). Deleting a chunk **cascades** to its whole sub-tree. |
| `Type` | `ChunkType` (enum) | See below. |
| `Order` | `int` | Position within the document, in document reading order. |
| `Content` | `string?` | The chunk's text. |
| `Topics` | `List<string>` | Document-level topics, copied onto every chunk of that document. Stored as a JSON text column. |
| `Tags` | `List<string>` | Chunk-specific tags. Stored as a JSON text column. |
| `Embedding` | `float[]` | 384-dimensional vector from the local embedding model. Stored as a JSON text column. Never returned in API responses (see `ChunkMappings`) — it's ranking input only. |
| `Metadata` | `Dictionary<string,string>` | Free-form key/value bag, currently unused by parsers but available for future extension. Stored as a JSON text column. |
| `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | UTC timestamps. |

`ChunkType` ([`Enums.cs`](ContextEngine.Api/Enums.cs)) is the structural role a chunk plays: `Document, Section, Heading, Paragraph, List, ListItem, Table, TableRow, TableCell, Definition, Quote, Note, Warning, Footnote, Reference, Code, Unknown`. Only `Heading`, `Paragraph` and `Table` are actually produced by the current parsers; the rest of the enum exists for richer future parsing.

**Why JSON columns instead of proper relational tables for `Topics`/`Tags`/`Metadata`/`Embedding`**: SQLite has no native array/map column type, so EF Core serializes these `List<T>`/`Dictionary<K,V>` properties to a single `TEXT` column per property (configured in `ContextEngineDbContext.OnModelCreating`, with custom `ValueComparer`s so EF's change tracking still works on the deserialized collections). The trade-off — and the biggest scalability caveat in this codebase — is that **`Topics`/`Tags`/date-range filters cannot be pushed down to SQL**; every query that filters on them loads the full `Chunks` table into memory first (see `SearchService.SearchAsync`, `ChunkService.GetChunksByTopicAsync`, etc.). Fine at prototype scale, but the first thing to revisit if the chunk count grows large — see [Known limitations](#known-limitations).

Alongside `Chunk`, `ContextEngineDbContext` also owns the full **ASP.NET Core Identity** schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, etc.) — application data and account data live in the same SQLite file.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- The [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) global tool, to apply database migrations:
  ```bash
  dotnet tool install --global dotnet-ef
  ```
- An Anthropic API key, set as the `ANTHROPIC_API_KEY` environment variable. Required only for `POST /api/documents` (topic/tag generation calls Claude); every other endpoint, including semantic search, works without it since embeddings are computed locally.
- No embedding-related setup needed: the `all-MiniLM-L6-v2` ONNX model (~23 MB) is already checked into `ContextEngine.Api/EmbeddingModel/` and copied to the output directory on build.

## Getting started

```bash
# 1. Restore & build
dotnet build ContextEngine.Api.sln

# 2. Create/update the local SQLite database (application schema + Identity schema)
dotnet ef database update --project ContextEngine.Api --startup-project ContextEngine.Api

# 3. Set your Anthropic key (only needed for document upload)
setx ANTHROPIC_API_KEY "sk-ant-..."      # Windows, persists across sessions (new shell needed to take effect)
# export ANTHROPIC_API_KEY="sk-ant-..." # macOS/Linux/WSL, current shell only

# 4. Run
dotnet run --project ContextEngine.Api
```

The API starts at `https://localhost:7056` (and `http://localhost:5209`) per [`Properties/launchSettings.json`](ContextEngine.Api/Properties/launchSettings.json), and opens Swagger UI at `/swagger` automatically when `ASPNETCORE_ENVIRONMENT=Development` (the default for `dotnet run`).

> **The database is not migrated automatically at startup.** `Program.cs` deliberately doesn't call `Database.Migrate()` — step 2 above must be rerun by hand any time a new migration is added (check [`ContextEngine.Api/Migrations/`](ContextEngine.Api/Migrations/) for the current list). Skipping this step is the most common source of confusing 500 errors on `/register` or `/login` — the symptom is a generic `"An unexpected error occurred."` because `AspNetUsers` doesn't exist yet.

### Common setup issues

| Symptom | Cause | Fix |
|---|---|---|
| `POST /register` / `POST /login` returns 500 | Migrations not applied to `ContextEngine.db` | Run the `dotnet ef database update` command above |
| `POST /api/documents` throws an Anthropic auth error | `ANTHROPIC_API_KEY` not set (or set in a shell you didn't restart) | Set the env var, open a **new** terminal, retry |
| `POST /api/documents` returns 400 `"No parser registered for file type: ..."` | File extension isn't `.docx` or `.pdf` | Only those two formats are supported today |
| Every `/api/*` call returns 401 | No/invalid `Authorization: Bearer <token>` header | See [Authentication & authorization](#authentication--authorization) |

## Configuration

Standard ASP.NET Core layered configuration — [`appsettings.json`](ContextEngine.Api/appsettings.json) (base) overlaid by [`appsettings.Development.json`](ContextEngine.Api/appsettings.Development.json) in dev, overlaid by environment variables/user secrets.

| Key | Where | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `appsettings.Development.json` only | SQLite connection string, `Data Source=ContextEngine.db` — a file relative to the working directory. There's no production connection string checked in; set one via environment variable (`ConnectionStrings__DefaultConnection`) or user secrets when deploying. |
| `ANTHROPIC_API_KEY` | Environment variable (not `appsettings.json`) | Read directly by the `Anthropic` SDK's `AnthropicClient` constructor in `Program.cs` — never stored in configuration files, so it can't accidentally end up committed. |
| `Logging:LogLevel` | Both files | Standard ASP.NET Core log level configuration. |

There is intentionally **no `appsettings.Production.json`** checked in — production configuration (connection string, allowed hosts, etc.) is expected to come from environment variables or a secrets store at deploy time, not from a file in source control.

## Authentication & authorization

Every endpoint under `/api/*` requires a bearer token. Authentication is handled entirely by **ASP.NET Core Identity's built-in minimal-API endpoints** (`MapIdentityApi<ApplicationUser>()` in `Program.cs`) — there is no hand-written `AuthController` in this codebase; `/register`, `/login`, `/refresh`, `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa` and `/manage/info` all come from the framework for free.

**Why bearer tokens and not cookies**: `AddIdentityApiEndpoints` defaults to `IdentityConstants.BearerScheme` (an opaque, server-validated token, not a JWT), which fits a stateless API consumed by scripts/agents better than cookie-based auth, which assumes a browser. No extra NuGet package (e.g. a JWT library) was needed for this — it's built into `Microsoft.AspNetCore.Identity` as of .NET 8.

**Authorization model**: flat — any authenticated user can call any endpoint. There are no roles or per-user data ownership (`Chunk` has no owning-user column). This matches the single-tenant, local-tool nature of the app; see [Known limitations](#known-limitations) if you need multi-user isolation.

**Password policy**: ASP.NET Core Identity's defaults — minimum 6 characters, at least one digit, one lowercase letter, one uppercase letter, and one non-alphanumeric character. `RequireConfirmedAccount` is left at its default (`false`), so `/register` immediately creates a usable account with no email-confirmation step (no email sender is configured).

### Full flow

```bash
# 1. Register (once per account)
curl -X POST https://localhost:7056/register \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Some-Strong-Password1!"}'
# -> 200 OK, empty body

# 2. Log in to obtain a token
curl -X POST https://localhost:7056/login \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Some-Strong-Password1!"}'
# -> 200 OK
# {"tokenType":"Bearer","accessToken":"CfDJ8...","expiresIn":3600,"refreshToken":"CfDJ8..."}

# 3. Call any protected endpoint
curl https://localhost:7056/api/search \
  -H "Authorization: Bearer CfDJ8..."

# 4. Once the access token expires (after `expiresIn` seconds), get a new one without re-entering
#    the password:
curl -X POST https://localhost:7056/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"CfDJ8..."}'
```

If the token is missing, malformed, or expired, protected endpoints return **`401 Unauthorized`** before the controller action ever runs (enforced by the `[Authorize]` attribute + the `AddAuthorization()`/`UseAuthentication()`/`UseAuthorization()` pipeline in `Program.cs`).

### Using Swagger UI

1. Open `/swagger`.
2. Expand `POST /register`, fill in `email`/`password`, **Execute**.
3. Expand `POST /login`, same credentials, **Execute**. Copy the `accessToken` from the response.
4. Click the **Authorize** button (top right, padlock icon).
5. Paste `Bearer <accessToken>` into the value field and confirm.
6. Every request Swagger UI sends from then on carries the `Authorization` header automatically.

This works because `Program.cs` registers an `OpenApiSecurityScheme` named `"Bearer"` and a matching global `OpenApiSecurityRequirement` in the `AddSwaggerGen(...)` call.

## API reference

All endpoints below require a valid bearer token (see above) unless stated otherwise. Response bodies use `ChunkDto` shape unless noted.

### `ChunkDto` shape (read model)

```jsonc
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "parentId": null,
  "type": "Paragraph",           // string, not integer — see Architecture > Enums over the wire
  "order": 3,
  "content": "Invoices are due within 14 days of receipt.",
  "topics": ["Billing"],
  "tags": ["payment-terms"],
  "metadata": {},
  "createdAt": "2026-08-27T14:00:00Z",
  "updatedAt": "2026-08-27T14:00:00Z"
}
```

Note: `embedding` is **not** included — it's internal ranking input, not client-facing data.

### Documents — `/api/documents`

| Method & route | Description | Request | Response |
|---|---|---|---|
| `POST /api/documents?documentPath={path}` | Parses the file at the given **server-local** path and persists its chunks (see [How document parsing works](#how-document-parsing-works)). `documentPath` is a query string parameter, not a file upload — the API reads a file the server process already has access to. | — | `200 OK` with the new document's `Guid` (`sourceId`) |
| `GET /api/documents/{documentId}` | All chunks belonging to a document, in document order. | — | `200 OK` → `ChunkDto[]`, or `404` if no chunk has that `SourceId` |
| `GET /api/documents/by-topic/{topic}` | Ids of documents that have at least one chunk tagged with `topic`. | — | `200 OK` → `Guid[]` (possibly empty) |
| `GET /api/documents/by-tag/{tag}` | Ids of documents that have at least one chunk tagged with `tag`. | — | `200 OK` → `Guid[]` |
| `GET /api/documents/by-date-range?startDate=yyyy-MM-dd&endDate=yyyy-MM-dd` | Ids of documents with at least one chunk created in `[startDate, endDate]` (inclusive, UTC). | — | `200 OK` → `Guid[]` |
| `DELETE /api/documents/{documentId}` | Deletes a document and every one of its chunks. | — | `204 No Content`, or `404` if the document doesn't exist |

### Chunks — `/api/chunks`

| Method & route | Description | Request | Response |
|---|---|---|---|
| `GET /api/chunks/{chunkId}` | A single chunk. | — | `200 OK` → `ChunkDto`, or `404` |
| `GET /api/chunks/by-document/{documentId}` | All chunks of a document, in order. | — | `200 OK` → `ChunkDto[]`, or `404` if the document has no chunks |
| `GET /api/chunks/by-topic/{topic}` | Chunks tagged with `topic` (across all documents). | — | `200 OK` → `ChunkDto[]` (empty array if none match — no 404) |
| `GET /api/chunks/by-tag/{tag}` | Chunks tagged with `tag`. | — | `200 OK` → `ChunkDto[]` |
| `GET /api/chunks/by-date-range?startDate=yyyy-MM-dd&endDate=yyyy-MM-dd` | Chunks created in the given range. | — | `200 OK` → `ChunkDto[]` |
| `PUT /api/chunks/{chunkId}` | Replaces a chunk's `Content`, `Type`, `Order`, `Topics`, `Tags`, `Metadata` in place; also bumps `UpdatedAt`. | Body: `ChunkDto` | `200 OK` → updated `ChunkDto`, or `404` |
| `DELETE /api/chunks/{chunkId}` | Deletes a chunk **and its whole sub-tree** (cascade, per the `Parent`/`Children` self-reference). | — | `204 No Content`, or `404` |

> There's currently no `POST /api/chunks` — chunks are only ever created as a side effect of `POST /api/documents`. This is intentional today (no standalone-chunk-creation use case yet), not an oversight to route around.

### Search — `/api/search`

| Method & route | Description | Request | Response |
|---|---|---|---|
| `POST /api/search` | Filters chunks by `Types`/`Topics`/`Tags`, then ranks the survivors by semantic similarity to `Query` (or returns them in storage order if `Query` is blank). Capped at 20 results. | Body: `SearchRequest` (below) | `200 OK` → `SearchResponse` (`{ "chunks": ChunkDto[] }`) |
| `GET /api/search` | The distinct `Types`/`Topics`/`Tags` currently present across all stored chunks — call this first to know which filter values will actually match something. | — | `200 OK` → `SearchableOptionsResponse` |

`SearchRequest` body:

```jsonc
{
  "query": "when are invoices due",   // required; blank => no semantic ranking, just filtering
  "types": ["Paragraph", "Table"],    // optional; null/omitted => no type filter
  "topics": ["Billing"],              // optional; OR semantics — matches ANY listed topic
  "tags": []                          // optional; OR semantics — matches ANY listed tag
}
```

`SearchableOptionsResponse`:

```jsonc
{
  "types": ["Heading", "Paragraph", "Table"],
  "topics": ["Billing", "Onboarding"],
  "tags": ["payment-terms", "step-1"]
}
```

## How document parsing works

Both parsers implement the same contract — `Task<List<CreateChunkDto>> ParseAsync(string filePath)` — and both call `IAiHelper` internally at the end of parsing (see [How AI enrichment works](#how-ai-enrichment-works)) before returning. `DocumentService.UploadDocumentAsync` picks a parser purely by file extension.

**`DocxParser`** ([`Parsers/DocxParser.cs`](ContextEngine.Api/Parsers/DocxParser.cs)) walks the Word document body via the Open XML SDK:
- A paragraph styled `HeadingX` (any level) → `ChunkType.Heading`; any other non-empty paragraph → `ChunkType.Paragraph`.
- A table → one `ChunkType.Table` chunk holding the table's full inner text (not yet split into rows/cells — see [Known limitations](#known-limitations)).
- Empty paragraphs are skipped.

**`PdfParser`** ([`Parsers/PdfParser.cs`](ContextEngine.Api/Parsers/PdfParser.cs)) has a harder problem: PDF has no semantic structure at all, just glyphs with X/Y coordinates. It reconstructs structure with two heuristics:
- **Heading detection by font size**: the most frequently occurring font size in the whole document is treated as "body text" size; a line whose average font size is at least 1.2× that is classified as a heading (`HeadingFontSizeMultiplier`).
- **Paragraph reconstruction by line gap**: the most frequently occurring vertical gap between consecutive lines is treated as "single-line spacing"; consecutive non-heading lines are merged into one paragraph chunk as long as their gap stays within 1.5× that typical gap (`ParagraphBreakGapMultiplier`) — a bigger gap starts a new paragraph. This resets at each page boundary (paragraphs are never merged across pages, since Y coordinates restart per page).

Both heuristics are intentionally simple and font/layout-dependent — they work well on conventionally-formatted documents (consistent body font, normal paragraph spacing) and can misclassify unusual layouts (multi-column PDFs, decorative fonts, scanned/image-only PDFs with no text layer at all).

### Heading-based nesting

Both parsers build a real tree, not a flat list — every non-heading chunk gets `ParentId` set to the most recent heading open at that point, and a heading nests under the most recent heading of a strictly outer (lower) level:

- **`DocxParser`** reads the level straight from the style id (`Heading1` → level 1, `Heading2` → level 2, ...; a heading style with no trailing digit defaults to level 1).
- **`PdfParser`** has no such explicit level, since PDF headings are just "a line whose font is big enough" — so it derives one: every distinct heading font size found in the document is ranked largest-first into levels 1, 2, 3, ... (`GetHeadingLevelsBySize`), the PDF analogue of Word's `Heading1`/`Heading2` styles. This ancestry persists across page breaks (unlike paragraph line-merging, which resets per page — see above), so a section opened on one page still parents the chunks at the top of the next.

Both parsers share the same ancestry rule (`BuildAncestry` in each): hitting a new heading pops every currently-open heading whose level is `>=` the new one's (a same-level heading is a sibling, ending the previous one's scope; a higher-numbered/smaller heading below it is irrelevant since it's already been popped) but leaves lower-level/larger ancestors open, then parents the new heading under whatever remains on top. A `Heading2` nests under the preceding `Heading1`; a second `Heading1` closes out any open `Heading2`s *and* the first `Heading1`, becoming a new top-level sibling.

Chunk identity for this to work is assigned by the parser itself, not at persistence time: `CreateChunkDto.Id` is generated when a chunk is created (so a later sibling/child can reference it as `ParentId` before anything is saved), and `ChunkMappings.MapDtosToChunks` carries that id through unchanged rather than generating a fresh one.

## How AI enrichment works

`AiHelper` ([`Services/AiHelper.cs`](ContextEngine.Api/Services/AiHelper.cs)) makes two Claude calls per uploaded document, both using model `claude-opus-5` at `Effort.Low` (a cheap classification task, not worth a larger model or more reasoning effort) with a JSON output schema that constrains the response shape:

1. **`CreateTopicsAsync`** — one call, given the whole document's concatenated text, asking for 1–5 short topics. The prompt lists every topic already in use elsewhere in the system (from `SearchService.GetSearchableOptionsAsync`) and instructs the model to reuse one of them when it's a genuinely good fit, only inventing a new one otherwise — this keeps the topic vocabulary from fragmenting into near-duplicates across documents (e.g. "Billing" vs. "Invoicing" vs. "Payments").
2. **`CreateTagsAsync`** — one call, given every chunk indexed (`Chunk 0: ...`, `Chunk 1: ...`) so the model has whole-document context, asking for 1–5 tags per chunk, keyed back by index. Same reuse-existing-values instruction as topics.

If the document has no non-blank content, both calls are skipped and empty topics/tags are returned — no wasted API call. `DocxParser`/`PdfParser` call these two methods sequentially (topics, then tags) right before returning their parsed chunks; `DocumentService` computes embeddings afterward, once topics/tags are already attached.

## How semantic search works

`SearchService.SearchAsync` ([`Services/SearchService.cs`](ContextEngine.Api/Services/SearchService.cs)) runs in two stages:

1. **Filter**: `Types` is a plain scalar column, so it's filtered in SQL. `Topics`/`Tags` are JSON columns (see [Data model](#data-model)), so every chunk surviving the `Types` filter is loaded into memory and checked against the requested topics/tags with **OR** semantics (a chunk matches if it has *any* of the requested topics, and *any* of the requested tags — not all of them).
2. **Rank**: if `Query` is blank, the filtered candidates are simply truncated to 20 in their existing storage order. Otherwise, the query text is embedded with the same local ONNX model used at ingestion time, and every candidate is scored by **cosine similarity** (`OnnxEmbeddingService.CosineSimilarity`) between its stored embedding and the query embedding, sorted descending, and capped at `SearchService.MaxResults = 20`.

Cosine similarity ranges from -1 to 1; a length mismatch or zero-magnitude vector (e.g. a chunk seeded directly into the DB without ever going through the embedding step) scores exactly 0 rather than throwing, so one bad vector can't fail an entire search.

## Project structure

```
ContextEngine.Api.sln
README.md, README.sk.md              This document and its Slovak counterpart

ContextEngine.Api/                    API project (net8.0, ASP.NET Core Web API)
  Program.cs                          DI registration, middleware pipeline, Identity/Swagger wiring
  ContextEngineDbContext.cs           EF Core context: Chunk + full Identity schema
  GlobalExceptionHandler.cs           Central exception -> ProblemDetails mapping
  Enums.cs                            ChunkType

  Controllers/
    DocumentsController.cs            POST/GET/DELETE for whole documents
    ChunksController.cs               GET/PUT/DELETE for individual chunks
    SearchController.cs               POST search, GET searchable options

  Services/
    DocumentService.cs                Upload/parse/embed/persist pipeline; document-level queries
    ChunkService.cs                   CRUD + filtering on individual chunks
    SearchService.cs                  Filter + cosine-similarity ranking
    AiHelper.cs                       Claude calls for topics/tags
    OnnxEmbeddingService.cs           Local embedding + cosine similarity
    Interfaces/                       IDocumentService, IChunkService, ISearchService, IAiHelper, IEmbeddingService

  Parsers/
    DocxParser.cs                     .docx -> chunks (Open XML SDK)
    PdfParser.cs                      .pdf -> chunks (PdfPig + font-size/line-gap heuristics)
    Interfaces/                       IDocxParser, IPdfParser

  Models/
    Chunk/Chunk.cs                    The Chunk entity (see Data model)
    Identity/ApplicationUser.cs       ASP.NET Core Identity user
    Requests/SearchRequest.cs
    Responses/SearchResponse.cs, SearchableOptionsResponse.cs

  DTOs/
    ChunkDto.cs                       Read model (API responses)
    CreateChunkDto.cs                 Parser output / pre-persistence write model

  Mappings/
    ChunkMappings.cs                  Chunk <-> ChunkDto/CreateChunkDto conversion

  Migrations/                         EF Core migrations (application schema + Identity schema)
  EmbeddingModel/                     Bundled local ONNX model (model.onnx, vocab.txt, NOTICE.txt)
  Properties/launchSettings.json      Local run profiles (ports, dev environment)
  appsettings.json, appsettings.Development.json

ContextEngine.Api.Tests/              xUnit test project
  Unit/                                Services/, Parsers/, Mappings/ - pure unit tests (Moq-based fakes)
  Api/                                 In-process integration tests (WebApplicationFactory<Program>)
  TestHelpers/                         FakeAiHelper, TestAuthHandler, TestDocuments, EmbeddingServiceFixture
```

## Testing

```bash
dotnet test ContextEngine.Api.sln
```

103 tests, split into two kinds:

- **Unit tests** (`ContextEngine.Api.Tests/Unit/`) — services, parsers and mappings tested in isolation with mocked dependencies (Moq). Cover `ChunkService`, `DocumentService`, `SearchService`, `OnnxEmbeddingService`, `DocxParser`, `PdfParser`, `ChunkMappings`, and `GlobalExceptionHandler`.
- **API integration tests** (`ContextEngine.Api.Tests/Api/`) — boot the whole app in-process via `WebApplicationFactory<Program>` (see `ContextEngineApiFactory`), against a fresh temp-file SQLite database per test class, with the real `IAiHelper` swapped for a no-op `FakeAiHelper` (no network calls, no API key needed to run the suite). By default these tests also bypass authentication via `TestAuthHandler` — a fake scheme that authenticates every request as a fixed test user — so `ChunksControllerApiTests`, `DocumentsControllerApiTests` and `SearchControllerApiTests` can focus purely on business-logic behavior instead of token plumbing.
  - `AuthenticationApiTests` is the exception: it sets `ContextEngineApiFactory.BypassAuthentication = false` and exercises the *real* flow — `401` with no/invalid token, `401` on wrong password, and a full register → login → authorized-request round trip.

## Known limitations

These are conscious trade-offs for a prototype/local-tool stage, not bugs — listed here so they're a deliberate choice to revisit, not a surprise later:

- **`Topics`/`Tags`/date-range filtering loads the whole `Chunks` table into memory** (see [Data model](#data-model)). Fine at hundreds/low-thousands of chunks; the first thing to fix (normalize `Topics`/`Tags` into their own join tables) if the dataset grows meaningfully.
- **No automatic migration on startup.** `dotnet ef database update` must be run by hand after pulling a new migration. Deliberate — auto-migrating in `Program.cs` is a common footgun in multi-instance deployments — but worth automating in a deploy script for this single-instance use case.
- **`POST /api/documents` takes a server-local file path**, not a multipart upload. This is by design for a local tool the caller (e.g. an AI agent) and the API share a filesystem with, but it means the caller can make the server read *any* file it has OS-level permission to read — don't expose this endpoint beyond a trusted local/private network without adding path validation.
- **Flat authorization**: any authenticated user can read/write/delete any chunk or document — there's no per-user data ownership. Fine for a single-operator tool; would need an owning-user column + authorization checks for a genuinely multi-tenant deployment.
- **Tables aren't structurally parsed.** A `Table` chunk holds the entire table's text as one blob (`InnerText`), not individual rows/cells — `ChunkType.TableRow`/`TableCell` exist in the enum but nothing produces them yet. A table also isn't nested under a heading the way paragraphs are in `DocxParser` — see its source for the current handling.
- **`PdfParser`'s heading levels are inferred from font size**, not an explicit outline — a document that (unusually) uses the *same* font size for two conceptually different heading levels will have them collapse into one level in the tree; a document with meaningful visual variation in in-body text size (e.g. a large pull-quote) could be misread as an extra heading level. This is a natural extension of the existing font-size heading heuristic, with the same honest caveat: it works well for conventionally-formatted PDFs and can misclassify unusual layouts.

## License notes

The bundled embedding model (`ContextEngine.Api/EmbeddingModel/model.onnx`, `vocab.txt`) is redistributed unmodified from [Xenova/all-MiniLM-L6-v2](https://huggingface.co/Xenova/all-MiniLM-L6-v2) (itself an ONNX export of [sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)) under the Apache License 2.0 — see [`EmbeddingModel/NOTICE.txt`](ContextEngine.Api/EmbeddingModel/NOTICE.txt) for the full attribution.
