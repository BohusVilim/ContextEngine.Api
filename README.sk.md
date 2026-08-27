# ContextEngine.Api

*[English version: [README.md](README.md)]*

Lokálne ASP.NET Core Web API, ktoré premieňa Word/PDF dokumenty na prehľadávateľnú, štruktúrovanú znalostnú bázu pre AI agentov. Dokument rozparsuje na strom chunkov (nadpisy, odseky, tabuľky), každý chunk otaguje topicmi/tagmi cez Claude, lokálne ho zvektorizuje pre sémantické vyhľadávanie a všetko sprístupní cez malé REST API.

## Čo appka robí

1. **Upload** `.docx` alebo `.pdf` súboru (podľa lokálnej cesty).
2. Príslušný parser (`DocxParser` / `PdfParser`) rozdelí dokument na **chunky** — nadpisy, odseky, tabuľky — pri zachovaní poradia a štruktúry.
3. Chunky celého dokumentu sa pošlú **Claude** (cez `Anthropic` SDK), ktorá odvodí 1–5 **topics** pre celý dokument a 1–5 **tags** pre každý chunk, pričom sa snaží znovupoužiť už existujúce hodnoty, ak sa hodia.
4. Text každého chunku sa lokálne zvektorizuje modelom **all-MiniLM-L6-v2**, ktorý beží úplne on-device cez ONNX Runtime (žiadne cloudové embedding API, žiadny kľúč potrebný pre tento krok).
5. Chunky sa ukladajú do **SQLite** (cez EF Core) a dajú sa načítať, filtrovať (podľa dokumentu, topicu, tagu, dátumového rozsahu) alebo **sémanticky vyhľadávať** cez cosine similarity voči embeddingu dopytu.

## Technológie

- **.NET 8** / ASP.NET Core Web API
- **Entity Framework Core 8** + SQLite
- **ASP.NET Core Identity** (bearer tokeny) pre autentifikáciu/autorizáciu
- **Microsoft.SemanticKernel.Connectors.Onnx** pre lokálne embeddingy
- **Anthropic SDK** (Claude) pre generovanie topics/tags
- **DocumentFormat.OpenXml** (.docx) a **PdfPig** (.pdf) na parsovanie
- **Swashbuckle** (Swagger/OpenAPI)
- **xUnit** pre testy (unit aj in-process API integračné testy)

## Predpoklady

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Nástroj [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) na aplikovanie databázových migrácií:
  ```bash
  dotnet tool install --global dotnet-ef
  ```
- Anthropic API kľúč, nastavený ako premenná prostredia `ANTHROPIC_API_KEY`. Potrebný pre `POST /api/documents` (generovanie topics/tags); všetko ostatné funguje aj bez neho.

## Spustenie

```bash
# 1. Restore & build
dotnet build ContextEngine.Api.sln

# 2. Vytvorenie/aktualizácia lokálnej SQLite databázy (dáta appky + Identity tabuľky)
dotnet ef database update --project ContextEngine.Api --startup-project ContextEngine.Api

# 3. Nastavenie Anthropic kľúča (potrebné len pre upload dokumentov)
setx ANTHROPIC_API_KEY "sk-ant-..."      # Windows, ostane nastavené aj po reštarte
# export ANTHROPIC_API_KEY="sk-ant-..." # macOS/Linux/WSL, len pre aktuálny shell

# 4. Spustenie
dotnet run --project ContextEngine.Api
```

API beží na `https://localhost:7056` (a `http://localhost:5209`) a vo vývojovom režime automaticky otvorí Swagger UI na `/swagger`.

> Databáza sa pri štarte automaticky nemigruje — krok 2 vyššie treba spustiť znova po každom pulle zmien, ktoré pridávajú novú migráciu (skontroluj `ContextEngine.Api/Migrations/`).

## Autentifikácia

Každý endpoint pod `/api/*` vyžaduje bearer token. Samotné endpointy na tokeny (`/register`, `/login`, `/refresh`, ...) poskytuje "z krabice" ASP.NET Core Identity cez `MapIdentityApi` — v kóde teda nenájdeš žiadny vlastný `AuthController`.

```bash
# Registrácia (raz)
curl -X POST https://localhost:7056/register \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Silne-Heslo1!"}'

# Prihlásenie, získanie tokenu
curl -X POST https://localhost:7056/login \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Silne-Heslo1!"}'
# -> {"tokenType":"Bearer","accessToken":"...","expiresIn":3600,"refreshToken":"..."}

# Volanie chráneného endpointu
curl https://localhost:7056/api/search \
  -H "Authorization: Bearer <accessToken>"
```

Access token platí 1 hodinu (`expiresIn`); na nový token bez opätovného prihlásenia použi `/refresh` s `refreshToken`.

V Swagger UI klikni na **Authorize** a vlož token — odteraz sa priloží ku každému requestu odoslanému odtiaľ.

## Prehľad API

| Oblasť | Endpoint | Popis |
|---|---|---|
| Documents | `POST /api/documents` | Rozparsuje a uloží dokument podľa lokálnej cesty |
| Documents | `GET /api/documents/{id}` | Vráti chunky dokumentu |
| Documents | `GET /api/documents/by-topic/{topic}`, `by-tag/{tag}`, `by-date-range` | Filtrovanie id dokumentov |
| Documents | `DELETE /api/documents/{id}` | Zmaže dokument a jeho chunky |
| Chunks | `GET /api/chunks/{id}` | Vráti jeden chunk |
| Chunks | `GET /api/chunks/by-document/{id}`, `by-topic/{topic}`, `by-tag/{tag}`, `by-date-range` | Filtrovanie chunkov |
| Chunks | `PUT /api/chunks/{id}` | Aktualizuje chunk |
| Chunks | `DELETE /api/chunks/{id}` | Zmaže chunk (a jeho podstrom) |
| Search | `POST /api/search` | Sémantické vyhľadávanie s voliteľnými filtrami na typ/topic/tag |
| Search | `GET /api/search` | Vráti zoznam topics/tags/typov, podľa ktorých sa dá aktuálne filtrovať |

Presné tvary requestov/response nájdeš v Swagger UI (`/swagger`).

## Štruktúra projektu

```
ContextEngine.Api/           API projekt
  Controllers/                REST endpointy (Documents, Chunks, Search)
  Services/                   Business logika (DocumentService, ChunkService, SearchService, AiHelper, OnnxEmbeddingService)
  Parsers/                    Docx/Pdf -> extrakcia chunkov
  Models/                     EF entity (Chunk) a Identity user (ApplicationUser)
  DTOs/, Mappings/            Tvary pre API a konverzia entity<->DTO
  Migrations/                 EF Core migrácie (schéma appky + Identity schéma)
  EmbeddingModel/              Zabalený lokálny ONNX embedding model (licencia v NOTICE.txt)
ContextEngine.Api.Tests/     xUnit testy (Unit/ a Api/ integračné testy)
```

## Spustenie testov

```bash
dotnet test ContextEngine.Api.sln
```

Integračné testy (`ContextEngine.Api.Tests/Api/`) spúšťajú API in-process nad dočasnou SQLite databázou a defaultne obchádzajú autentifikáciu (pozri `ContextEngineApiFactory`), aby sa mohli sústrediť na business logiku; `AuthenticationApiTests` naopak testuje reálny register/login/`[Authorize]` flow.

## Poznámka k licencii

Zabalený embedding model (`ContextEngine.Api/EmbeddingModel/model.onnx`, `vocab.txt`) je nezmenená redistribúcia z [Xenova/all-MiniLM-L6-v2](https://huggingface.co/Xenova/all-MiniLM-L6-v2) pod licenciou Apache License 2.0 — pozri [`EmbeddingModel/NOTICE.txt`](ContextEngine.Api/EmbeddingModel/NOTICE.txt).
