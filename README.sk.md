# ContextEngine.Api

*[English version: [README.md](README.md)]*

Lokálne ASP.NET Core Web API, ktoré premieňa Word/PDF dokumenty na prehľadávateľnú, štruktúrovanú znalostnú bázu pre AI agentov. Dokument rozparsuje na strom chunkov (nadpisy, odseky, tabuľky), každý chunk obohatí o AI-generované topics/tags, lokálne ho zvektorizuje pre sémantické vyhľadávanie a všetko sprístupní cez malé REST API chránené tokenom.

Appka je navrhnutá ako **lokálny, single-tenant backend** — napr. niečo, s čím komunikuje AI coding agent alebo osobný RAG nástroj na `localhost`, nie ako verejná multi-tenant služba. Viacero rozhodnutí nižšie (bearer-token auth bez rolí, upload podľa file path, in-memory filtrovanie) to odzrkadľuje.

## Obsah

- [Prehľad](#prehľad)
- [Architektúra](#architektúra)
- [Dátový model](#dátový-model)
- [Predpoklady](#predpoklady)
- [Spustenie](#spustenie)
- [Konfigurácia](#konfigurácia)
- [Autentifikácia a autorizácia](#autentifikácia-a-autorizácia)
- [API referencia](#api-referencia)
- [Ako funguje parsovanie dokumentov](#ako-funguje-parsovanie-dokumentov)
- [Ako funguje AI obohacovanie](#ako-funguje-ai-obohacovanie)
- [Ako funguje sémantické vyhľadávanie](#ako-funguje-sémantické-vyhľadávanie)
- [Štruktúra projektu](#štruktúra-projektu)
- [Testovanie](#testovanie)
- [Známe obmedzenia](#známe-obmedzenia)
- [Poznámka k licencii](#poznámka-k-licencii)

## Prehľad

Základná myšlienka: nasmeruj API na `.docx` alebo `.pdf` súbor a stane sa z neho prehľadávateľná znalosť.

```
 .docx / .pdf súbor
        │
        ▼
 ┌─────────────────┐   plochý zoznam chunkov (nadpisy, odseky, tabuľky),
 │ DocxParser /     │   v poradí dokumentu, so štrukturálnym Type + Order
 │ PdfParser        │──────────────────────────────────────────────────┐
 └─────────────────┘                                                   │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ AiHelper (Claude)     │  topics pre dokument
                                                          │ CreateTopicsAsync /   │  + tags pre chunk
                                                          │ CreateTagsAsync       │
                                                          └──────────────────────┘
                                                                        │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ OnnxEmbeddingService  │  384-rozmerný embedding
                                                          │ (lokálne, bez API)    │  pre každý chunk
                                                          └──────────────────────┘
                                                                        │
                                                                        ▼
                                                          ┌──────────────────────┐
                                                          │ SQLite (cez EF Core)  │  uložené Chunk riadky
                                                          └──────────────────────┘
                                                                        │
                                    GET /api/chunks/*, /api/documents/* │  POST /api/search
                                    (načítanie a filtrovanie)           │  (ranking cosine similarity)
                                                                        ▼
                                                                 AI agent / klient
```

Toto všetko sa deje synchrónne v rámci jedného volania `POST /api/documents` — kým vráti odpoveď, dokument je už kompletne rozparsovaný, otagovaný, zvektorizovaný a uložený.

## Architektúra

Kód sleduje bežnú vrstvenú štruktúru, všetko zapojené cez dependency injection v [`Program.cs`](ContextEngine.Api/Program.cs):

| Vrstva | Namespace | Zodpovednosť |
|---|---|---|
| **Controllers** | `ContextEngine.Api.Controllers` | Tenké HTTP adaptéry — spracujú route/query/body, zavolajú jednu metódu service, výsledok namapujú na status kód. Žiadna business logika. |
| **Services** | `ContextEngine.Api.Services` | Business logika: `DocumentService` (pipeline upload/parse/persist), `ChunkService` (CRUD/filtrovanie jednotlivých chunkov), `SearchService` (filter + ranking), `AiHelper` (volania Claude), `OnnxEmbeddingService` (lokálne embeddingy). |
| **Parsers** | `ContextEngine.Api.Parsers` | Extrakcia špecifická pre formát: `DocxParser` (Open XML SDK), `PdfParser` (PdfPig + heuristiky). Každý implementuje úzke rozhranie `IDocxParser`/`IPdfParser`, aby `DocumentService` nemusel poznať detaily formátu. |
| **Data / Models** | `ContextEngine.Api.Data`, `ContextEngine.Api.Models` | `ContextEngineDbContext` (EF Core, rozširuje `IdentityDbContext<ApplicationUser>`) a entita `Chunk`. |
| **DTOs / Mappings** | `ContextEngine.Api.DTOs`, `ContextEngine.Api.Mappings` | `ChunkDto` (čítanie), `CreateChunkDto` (výstup parsera, pred uložením) a `ChunkMappings`, ktoré konvertujú medzi entitou a DTO tvarmi. |

Každý service je registrovaný ako `Scoped` (jedna inštancia na HTTP request), okrem `IEmbeddingService`, ktorý je `Singleton` — ONNX model a jeho `InferenceSession` sa načítajú raz pri štarte (cez `AddBertOnnxEmbeddingGenerator` v `Program.cs`) a znovupoužívajú počas celej životnosti appky, keďže načítavať ho pri každom requeste by bolo zbytočné a samotný model je pri inferencii bezstavový/thread-safe.

Prierezové záležitosti:
- **Chyby**: `GlobalExceptionHandler` ([`GlobalExceptionHandler.cs`](ContextEngine.Api/GlobalExceptionHandler.cs)) je jediné miesto, kde sa neošetrené výnimky mapujú na HTTP status + telo [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `ProblemDetails`. `NotSupportedException` → 400, `NotImplementedException` → 501, `FileNotFoundException`/`DirectoryNotFoundException` → 404, všetko ostatné → 500 bez toho, aby sa klientovi prezradila správa (skutočná výnimka sa naďalej loguje na serveri).
- **Enumy cez sieť**: kontrolery serializujú enumy (napr. `ChunkType`) ako reťazcový názov, nie ako číslo, cez `JsonStringEnumConverter` registrovaný v `AddControllers().AddJsonOptions(...)` — API payloady tak ostávajú samopopisné aj pre AI agenta, ktorý ich číta bez znalosti schémy.
- **Autentifikácia**: `[Authorize]` na každom kontroleri, postavené na bearer tokenoch ASP.NET Core Identity — pozri [Autentifikácia a autorizácia](#autentifikácia-a-autorizácia).

## Dátový model

Existuje jedna hlavná entita, `Chunk` ([`Models/Chunk/Chunk.cs`](ContextEngine.Api/Models/Chunk/Chunk.cs)):

| Pole | Typ | Poznámka |
|---|---|---|
| `Id` | `Guid` | Primárny kľúč. |
| `SourceId` | `Guid` | Id dokumentu, ku ktorému chunk patrí. Všetky chunky z jedného volania `POST /api/documents` zdieľajú rovnaký `SourceId` — samostatná tabuľka `Document` neexistuje. |
| `ParentId` / `Parent` / `Children` | `Guid?` / `Chunk?` / `List<Chunk>` | Self-referencing strom (chunk môže byť vnorený pod iný). Zmazanie chunku **kaskádovito** zmaže celý jeho podstrom. Parsery aktuálne produkujú plochý zoznam (`ParentId` je pri uploade vždy null), takže dnes je rodič každého chunku prázdny — podpora stromu/vnárania existuje v schéme pre budúce vylepšenia parserov (napr. vnorenie odsekov pod ich nadpis). |
| `Type` | `ChunkType` (enum) | Pozri nižšie. |
| `Order` | `int` | Pozícia v dokumente, v poradí čítania. |
| `Content` | `string?` | Text chunku. |
| `Topics` | `List<string>` | Topics na úrovni dokumentu, skopírované na každý chunk daného dokumentu. Uložené ako JSON text stĺpec. |
| `Tags` | `List<string>` | Tagy špecifické pre chunk. Uložené ako JSON text stĺpec. |
| `Embedding` | `float[]` | 384-rozmerný vektor z lokálneho embedding modelu. Uložené ako JSON text stĺpec. Nikdy sa nevracia v API odpovediach (pozri `ChunkMappings`) — je to len vstup pre ranking. |
| `Metadata` | `Dictionary<string,string>` | Voľná key/value schránka, parsery ju aktuálne nepoužívajú, ale je pripravená na budúce rozšírenia. Uložené ako JSON text stĺpec. |
| `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | UTC časové značky. |

`ChunkType` ([`Enums.cs`](ContextEngine.Api/Enums.cs)) je štrukturálna rola, ktorú chunk zohráva: `Document, Section, Heading, Paragraph, List, ListItem, Table, TableRow, TableCell, Definition, Quote, Note, Warning, Footnote, Reference, Code, Unknown`. Súčasné parsery reálne produkujú len `Heading`, `Paragraph` a `Table`; zvyšok enumu existuje pre bohatšie budúce parsovanie.

**Prečo JSON stĺpce namiesto poriadnych relačných tabuliek pre `Topics`/`Tags`/`Metadata`/`Embedding`**: SQLite nemá natívny typ stĺpca pre pole/mapu, takže EF Core serializuje tieto `List<T>`/`Dictionary<K,V>` vlastnosti do jedného `TEXT` stĺpca (nakonfigurované v `ContextEngineDbContext.OnModelCreating`, s vlastnými `ValueComparer`mi, aby zmenové sledovanie EF fungovalo aj na deserializovaných kolekciách). Kompromis — a najväčšie obmedzenie škálovateľnosti v tomto kóde — je, že **filtre na `Topics`/`Tags`/dátumový rozsah sa nedajú preložiť do SQL**; každý dopyt, ktorý na ne filtruje, najprv načíta celú tabuľku `Chunks` do pamäte (pozri `SearchService.SearchAsync`, `ChunkService.GetChunksByTopicAsync` atď.). Pri prototype je to v poriadku, ale je to prvá vec, ktorú treba riešiť, ak počet chunkov výrazne narastie — pozri [Známe obmedzenia](#známe-obmedzenia).

Popri `Chunk` má `ContextEngineDbContext` na starosti aj celú schému **ASP.NET Core Identity** (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, atď.) — dáta appky aj účtov žijú v tom istom SQLite súbore.

## Predpoklady

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Globálny nástroj [`dotnet-ef`](https://learn.microsoft.com/ef/core/cli/dotnet) na aplikovanie databázových migrácií:
  ```bash
  dotnet tool install --global dotnet-ef
  ```
- Anthropic API kľúč, nastavený ako premenná prostredia `ANTHROPIC_API_KEY`. Potrebný len pre `POST /api/documents` (generovanie topics/tags volá Claude); všetky ostatné endpointy vrátane sémantického vyhľadávania fungujú aj bez neho, keďže embeddingy sa počítajú lokálne.
- Netreba žiadny setup okolo embeddingov: model `all-MiniLM-L6-v2` v ONNX formáte (~23 MB) je už súčasťou repozitára v `ContextEngine.Api/EmbeddingModel/` a pri builde sa kopíruje do output priečinka.

## Spustenie

```bash
# 1. Restore & build
dotnet build ContextEngine.Api.sln

# 2. Vytvorenie/aktualizácia lokálnej SQLite databázy (schéma appky + schéma Identity)
dotnet ef database update --project ContextEngine.Api --startup-project ContextEngine.Api

# 3. Nastavenie Anthropic kľúča (potrebné len pre upload dokumentov)
setx ANTHROPIC_API_KEY "sk-ant-..."      # Windows, ostane nastavené aj po reštarte (treba nový shell)
# export ANTHROPIC_API_KEY="sk-ant-..." # macOS/Linux/WSL, len pre aktuálny shell

# 4. Spustenie
dotnet run --project ContextEngine.Api
```

API beží na `https://localhost:7056` (a `http://localhost:5209`) podľa [`Properties/launchSettings.json`](ContextEngine.Api/Properties/launchSettings.json) a automaticky otvorí Swagger UI na `/swagger`, keď je `ASPNETCORE_ENVIRONMENT=Development` (predvolené pre `dotnet run`).

> **Databáza sa pri štarte automaticky nemigruje.** `Program.cs` zámerne nevolá `Database.Migrate()` — krok 2 vyššie treba spustiť ručne vždy, keď pribudne nová migrácia (aktuálny zoznam v [`ContextEngine.Api/Migrations/`](ContextEngine.Api/Migrations/)). Vynechanie tohto kroku je najčastejší zdroj mätúcich 500 chýb na `/register` alebo `/login` — príznakom je všeobecné `"An unexpected error occurred."`, lebo tabuľka `AspNetUsers` ešte neexistuje.

### Časté problémy pri spustení

| Príznak | Príčina | Riešenie |
|---|---|---|
| `POST /register` / `POST /login` vráti 500 | Migrácie neaplikované na `ContextEngine.db` | Spusti príkaz `dotnet ef database update` vyššie |
| `POST /api/documents` vyhodí Anthropic auth chybu | `ANTHROPIC_API_KEY` nie je nastavený (alebo nastavený v shelli, ktorý si nereštartoval) | Nastav env premennú, otvor **nový** terminál, skús znova |
| `POST /api/documents` vráti 400 `"No parser registered for file type: ..."` | Prípona súboru nie je `.docx` ani `.pdf` | Dnes sú podporované len tieto dva formáty |
| Každé volanie `/api/*` vráti 401 | Chýbajúca/neplatná hlavička `Authorization: Bearer <token>` | Pozri [Autentifikácia a autorizácia](#autentifikácia-a-autorizácia) |

## Konfigurácia

Štandardná vrstvená konfigurácia ASP.NET Core — [`appsettings.json`](ContextEngine.Api/appsettings.json) (základ) prekrytý [`appsettings.Development.json`](ContextEngine.Api/appsettings.Development.json) vo vývoji, prekrytý premennými prostredia/user secrets.

| Kľúč | Kde | Význam |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Len `appsettings.Development.json` | SQLite connection string, `Data Source=ContextEngine.db` — súbor relatívny k pracovnému adresáru. Produkčný connection string nie je súčasťou repozitára; pri nasadení ho nastav cez premennú prostredia (`ConnectionStrings__DefaultConnection`) alebo user secrets. |
| `ANTHROPIC_API_KEY` | Premenná prostredia (nie `appsettings.json`) | Číta ju priamo konštruktor `AnthropicClient` z `Anthropic` SDK v `Program.cs` — nikdy sa neukladá do konfiguračných súborov, takže sa nemôže omylom commitnúť. |
| `Logging:LogLevel` | Oba súbory | Štandardná konfigurácia log levelu ASP.NET Core. |

Zámerne **chýba `appsettings.Production.json`** — očakáva sa, že produkčná konfigurácia (connection string, allowed hosts, atď.) príde z premenných prostredia alebo úložiska secretov pri nasadení, nie zo súboru vo verzovacom systéme.

## Autentifikácia a autorizácia

Každý endpoint pod `/api/*` vyžaduje bearer token. Autentifikáciu kompletne zabezpečujú **vstavané minimal-API endpointy ASP.NET Core Identity** (`MapIdentityApi<ApplicationUser>()` v `Program.cs`) — v kóde nie je žiadny ručne písaný `AuthController`; `/register`, `/login`, `/refresh`, `/confirmEmail`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa` a `/manage/info` sú z frameworku zadarmo.

**Prečo bearer tokeny a nie cookies**: `AddIdentityApiEndpoints` defaultne používa `IdentityConstants.BearerScheme` (nepriehľadný, serverom validovaný token, nie JWT), čo sa lepšie hodí pre bezstavové API konzumované skriptami/agentmi než cookie-based auth, ktorá predpokladá prehliadač. Na toto nebol potrebný žiadny extra NuGet balík (napr. JWT knižnica) — je to súčasť `Microsoft.AspNetCore.Identity` od .NET 8.

**Model autorizácie**: plochý — ktorýkoľvek prihlásený používateľ smie volať ktorýkoľvek endpoint. Neexistujú role ani vlastníctvo dát per-user (`Chunk` nemá stĺpec s vlastniacim používateľom). To zodpovedá povahe appky ako single-tenant lokálneho nástroja; pozri [Známe obmedzenia](#známe-obmedzenia), ak potrebuješ multi-user izoláciu.

**Politika hesiel**: defaultné nastavenia ASP.NET Core Identity — minimálne 6 znakov, aspoň jedna číslica, jedno malé písmeno, jedno veľké písmeno a jeden nealfanumerický znak. `RequireConfirmedAccount` ostáva na defaultnej hodnote (`false`), takže `/register` okamžite vytvorí použiteľný účet bez kroku potvrdenia emailom (email sender nie je nakonfigurovaný).

### Celý flow

```bash
# 1. Registrácia (raz na účet)
curl -X POST https://localhost:7056/register \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Silne-Heslo1!"}'
# -> 200 OK, prázdne telo

# 2. Prihlásenie, získanie tokenu
curl -X POST https://localhost:7056/login \
  -H "Content-Type: application/json" \
  -d '{"email":"me@example.com","password":"Silne-Heslo1!"}'
# -> 200 OK
# {"tokenType":"Bearer","accessToken":"CfDJ8...","expiresIn":3600,"refreshToken":"CfDJ8..."}

# 3. Volanie ľubovoľného chráneného endpointu
curl https://localhost:7056/api/search \
  -H "Authorization: Bearer CfDJ8..."

# 4. Keď access token vyprší (po `expiresIn` sekundách), získaj nový bez opätovného zadávania hesla:
curl -X POST https://localhost:7056/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"CfDJ8..."}'
```

Ak token chýba, je nesprávny alebo expirovaný, chránené endpointy vrátia **`401 Unauthorized`** ešte predtým, než sa vôbec spustí akcia kontrolera (vynucuje atribút `[Authorize]` + pipeline `AddAuthorization()`/`UseAuthentication()`/`UseAuthorization()` v `Program.cs`).

### Použitie cez Swagger UI

1. Otvor `/swagger`.
2. Rozbaľ `POST /register`, vyplň `email`/`password`, **Execute**.
3. Rozbaľ `POST /login`, rovnaké údaje, **Execute**. Skopíruj `accessToken` z odpovede.
4. Klikni na tlačidlo **Authorize** (vpravo hore, ikonka zámku).
5. Do poľa vlož `Bearer <accessToken>` a potvrď.
6. Odteraz každý request odoslaný zo Swagger UI automaticky nesie hlavičku `Authorization`.

Toto funguje vďaka tomu, že `Program.cs` v rámci `AddSwaggerGen(...)` registruje `OpenApiSecurityScheme` s názvom `"Bearer"` a zodpovedajúci globálny `OpenApiSecurityRequirement`.

## API referencia

Všetky endpointy nižšie vyžadujú platný bearer token (pozri vyššie), ak nie je uvedené inak. Telá odpovedí majú tvar `ChunkDto`, ak nie je uvedené inak.

### Tvar `ChunkDto` (read model)

```jsonc
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "parentId": null,
  "type": "Paragraph",           // reťazec, nie číslo — pozri Architektúra > Enumy cez sieť
  "order": 3,
  "content": "Faktúry sú splatné do 14 dní od doručenia.",
  "topics": ["Fakturácia"],
  "tags": ["platobné-podmienky"],
  "metadata": {},
  "createdAt": "2026-08-27T14:00:00Z",
  "updatedAt": "2026-08-27T14:00:00Z"
}
```

Poznámka: `embedding` **nie je** súčasťou odpovede — je to interný vstup pre ranking, nie dáta pre klienta.

### Documents — `/api/documents`

| Metóda a route | Popis | Request | Response |
|---|---|---|---|
| `POST /api/documents?documentPath={path}` | Rozparsuje súbor na danej **server-lokálnej** ceste a uloží jeho chunky (pozri [Ako funguje parsovanie dokumentov](#ako-funguje-parsovanie-dokumentov)). `documentPath` je query string parameter, nie file upload — API číta súbor, ku ktorému má prístup samotný serverový proces. | — | `200 OK` s `Guid` nového dokumentu (`sourceId`) |
| `GET /api/documents/{documentId}` | Všetky chunky patriace dokumentu, v poradí dokumentu. | — | `200 OK` → `ChunkDto[]`, alebo `404`, ak žiaden chunk nemá dané `SourceId` |
| `GET /api/documents/by-topic/{topic}` | Id dokumentov, ktoré majú aspoň jeden chunk otagovaný `topic`. | — | `200 OK` → `Guid[]` (môže byť prázdne) |
| `GET /api/documents/by-tag/{tag}` | Id dokumentov, ktoré majú aspoň jeden chunk otagovaný `tag`. | — | `200 OK` → `Guid[]` |
| `GET /api/documents/by-date-range?startDate=yyyy-MM-dd&endDate=yyyy-MM-dd` | Id dokumentov s aspoň jedným chunkom vytvoreným v rozsahu `[startDate, endDate]` (vrátane, UTC). | — | `200 OK` → `Guid[]` |
| `DELETE /api/documents/{documentId}` | Zmaže dokument a všetky jeho chunky. | — | `204 No Content`, alebo `404`, ak dokument neexistuje |

### Chunks — `/api/chunks`

| Metóda a route | Popis | Request | Response |
|---|---|---|---|
| `GET /api/chunks/{chunkId}` | Jeden chunk. | — | `200 OK` → `ChunkDto`, alebo `404` |
| `GET /api/chunks/by-document/{documentId}` | Všetky chunky dokumentu, v poradí. | — | `200 OK` → `ChunkDto[]`, alebo `404`, ak dokument nemá žiadne chunky |
| `GET /api/chunks/by-topic/{topic}` | Chunky otagované `topic` (naprieč všetkými dokumentmi). | — | `200 OK` → `ChunkDto[]` (prázdne pole, ak žiadny nesedí — nie 404) |
| `GET /api/chunks/by-tag/{tag}` | Chunky otagované `tag`. | — | `200 OK` → `ChunkDto[]` |
| `GET /api/chunks/by-date-range?startDate=yyyy-MM-dd&endDate=yyyy-MM-dd` | Chunky vytvorené v danom rozsahu. | — | `200 OK` → `ChunkDto[]` |
| `PUT /api/chunks/{chunkId}` | Nahradí `Content`, `Type`, `Order`, `Topics`, `Tags`, `Metadata` chunku; zároveň posunie `UpdatedAt`. | Body: `ChunkDto` | `200 OK` → aktualizovaný `ChunkDto`, alebo `404` |
| `DELETE /api/chunks/{chunkId}` | Zmaže chunk **a celý jeho podstrom** (kaskádovito, podľa self-referencie `Parent`/`Children`). | — | `204 No Content`, alebo `404` |

> Momentálne neexistuje `POST /api/chunks` — chunky vznikajú výhradne ako vedľajší efekt `POST /api/documents`. Toto je zámer (zatiaľ neexistuje use case pre samostatné vytváranie chunku), nie prehliadnutie.

### Search — `/api/search`

| Metóda a route | Popis | Request | Response |
|---|---|---|---|
| `POST /api/search` | Filtruje chunky podľa `Types`/`Topics`/`Tags`, potom zoradí prežívajúce podľa sémantickej podobnosti k `Query` (alebo ich vráti v poradí uloženia, ak je `Query` prázdne). Limit 20 výsledkov. | Body: `SearchRequest` (nižšie) | `200 OK` → `SearchResponse` (`{ "chunks": ChunkDto[] }`) |
| `GET /api/search` | Distinct `Types`/`Topics`/`Tags` aktuálne prítomné naprieč všetkými uloženými chunkami — zavolaj toto najprv, aby si vedel, ktoré filtrovacie hodnoty reálne niečo nájdu. | — | `200 OK` → `SearchableOptionsResponse` |

Telo `SearchRequest`:

```jsonc
{
  "query": "kedy su splatne faktury",   // povinné; prázdne => žiadny sémantický ranking, len filtrovanie
  "types": ["Paragraph", "Table"],      // voliteľné; null/vynechané => bez filtra na typ
  "topics": ["Fakturácia"],             // voliteľné; OR sémantika — sedí AKÝKOĽVEK z uvedených topics
  "tags": []                            // voliteľné; OR sémantika — sedí AKÝKOĽVEK z uvedených tags
}
```

`SearchableOptionsResponse`:

```jsonc
{
  "types": ["Heading", "Paragraph", "Table"],
  "topics": ["Fakturácia", "Onboarding"],
  "tags": ["platobné-podmienky", "krok-1"]
}
```

## Ako funguje parsovanie dokumentov

Oba parsery implementujú rovnaký kontrakt — `Task<List<CreateChunkDto>> ParseAsync(string filePath)` — a oba na konci parsovania interne volajú `IAiHelper` (pozri [Ako funguje AI obohacovanie](#ako-funguje-ai-obohacovanie)) predtým, ako vrátia výsledok. `DocumentService.UploadDocumentAsync` vyberá parser čisto podľa prípony súboru.

**`DocxParser`** ([`Parsers/DocxParser.cs`](ContextEngine.Api/Parsers/DocxParser.cs)) prechádza telo Word dokumentu cez Open XML SDK:
- Odsek so štýlom `HeadingX` (akákoľvek úroveň) → `ChunkType.Heading`; akýkoľvek iný neprázdny odsek → `ChunkType.Paragraph`.
- Tabuľka → jeden chunk `ChunkType.Table` obsahujúci celý text tabuľky (zatiaľ nerozdelený na riadky/bunky — pozri [Známe obmedzenia](#známe-obmedzenia)).
- Prázdne odseky sa preskakujú.

**`PdfParser`** ([`Parsers/PdfParser.cs`](ContextEngine.Api/Parsers/PdfParser.cs)) rieši ťažší problém: PDF nemá žiadnu sémantickú štruktúru, len glyfy so súradnicami X/Y. Štruktúru rekonštruuje dvomi heuristikami:
- **Detekcia nadpisu podľa veľkosti fontu**: najčastejšie sa vyskytujúca veľkosť fontu v celom dokumente sa berie ako veľkosť "bežného textu"; riadok, ktorého priemerná veľkosť fontu je aspoň 1,2-násobok tejto hodnoty, sa klasifikuje ako nadpis (`HeadingFontSizeMultiplier`).
- **Rekonštrukcia odsekov podľa medzery medzi riadkami**: najčastejšie sa vyskytujúca vertikálna medzera medzi po sebe idúcimi riadkami sa berie ako "riadkovanie"; po sebe idúce ne-nadpisové riadky sa zlúčia do jedného chunku odseku, pokiaľ ich medzera zostáva do 1,5-násobku tejto typickej medzery (`ParagraphBreakGapMultiplier`) — väčšia medzera začína nový odsek. Toto sa resetuje na každej hranici strany (odseky sa nikdy nezlučujú naprieč stranami, keďže Y súradnice sa na každej strane začínajú odznova).

Obe heuristiky sú zámerne jednoduché a závislé od fontu/layoutu — fungujú dobre na bežne formátovaných dokumentoch (konzistentný font tela, normálne riadkovanie) a môžu nesprávne klasifikovať nezvyčajné layouty (viacstĺpcové PDF, dekoratívne fonty, skenované/obrázkové PDF bez textovej vrstvy).

## Ako funguje AI obohacovanie

`AiHelper` ([`Services/AiHelper.cs`](ContextEngine.Api/Services/AiHelper.cs)) urobí pri uploade dokumentu dve volania Claude, obe s modelom `claude-opus-5` na úrovni `Effort.Low` (lacná klasifikačná úloha, na ktorú sa neoplatí väčší model ani väčšie reasoning effort), s JSON output schémou, ktorá obmedzuje tvar odpovede:

1. **`CreateTopicsAsync`** — jedno volanie, dostane spojený text celého dokumentu, žiada 1–5 krátkych topics. Prompt vymenuje všetky topics, ktoré sa už niekde v systéme používajú (z `SearchService.GetSearchableOptionsAsync`) a inštruuje model, aby jedno z nich znovupoužil, ak sa naozaj hodí, a nový vymyslel len vtedy, keď žiadne existujúce naozaj nesedí — to bráni fragmentácii slovníka topics na takmer-duplicity naprieč dokumentmi (napr. "Fakturácia" vs. "Faktúry" vs. "Platby").
2. **`CreateTagsAsync`** — jedno volanie, dostane každý chunk s indexom (`Chunk 0: ...`, `Chunk 1: ...`), aby mal model kontext celého dokumentu, žiada 1–5 tagov na chunk, viazaných späť podľa indexu. Rovnaká inštrukcia na znovupoužitie existujúcich hodnôt ako pri topics.

Ak dokument nemá žiadny neprázdny obsah, obe volania sa preskočia a vrátia sa prázdne topics/tags — bez zbytočného API volania. `DocxParser`/`PdfParser` volajú tieto dve metódy postupne (najprv topics, potom tags) tesne predtým, než vrátia svoje rozparsované chunky; `DocumentService` počíta embeddingy až potom, keď sú topics/tags už priradené.

## Ako funguje sémantické vyhľadávanie

`SearchService.SearchAsync` ([`Services/SearchService.cs`](ContextEngine.Api/Services/SearchService.cs)) beží v dvoch fázach:

1. **Filter**: `Types` je obyčajný skalárny stĺpec, takže sa filtruje v SQL. `Topics`/`Tags` sú JSON stĺpce (pozri [Dátový model](#dátový-model)), takže každý chunk, ktorý prežije filter `Types`, sa načíta do pamäte a skontroluje voči požadovaným topics/tags s **OR** sémantikou (chunk sedí, ak má *aspoň jeden* z požadovaných topics a *aspoň jeden* z požadovaných tags — nie všetky naraz).
2. **Ranking**: ak je `Query` prázdne, filtrovaní kandidáti sa jednoducho orežú na 20 v ich existujúcom poradí uloženia. Inak sa text dopytu zvektorizuje rovnakým lokálnym ONNX modelom, aký sa použil pri ukladaní, a každý kandidát sa ohodnotí **cosine similarity** (`OnnxEmbeddingService.CosineSimilarity`) medzi jeho uloženým embeddingom a embeddingom dopytu, zoradí zostupne a orežú na `SearchService.MaxResults = 20`.

Cosine similarity sa pohybuje od -1 do 1; nesúlad dĺžky alebo vektor s nulovou magnitúdou (napr. chunk vložený priamo do DB bez toho, aby prešiel krokom embeddingu) dostane skóre presne 0 namiesto vyhodenia výnimky, takže jeden zlý vektor nedokáže zhodiť celé vyhľadávanie.

## Štruktúra projektu

```
ContextEngine.Api.sln
README.md, README.sk.md              Tento dokument a jeho anglický náprotivok

ContextEngine.Api/                    API projekt (net8.0, ASP.NET Core Web API)
  Program.cs                          DI registrácia, middleware pipeline, zapojenie Identity/Swagger
  ContextEngineDbContext.cs           EF Core kontext: Chunk + celá schéma Identity
  GlobalExceptionHandler.cs           Centrálne mapovanie výnimiek na ProblemDetails
  Enums.cs                            ChunkType

  Controllers/
    DocumentsController.cs            POST/GET/DELETE pre celé dokumenty
    ChunksController.cs               GET/PUT/DELETE pre jednotlivé chunky
    SearchController.cs               POST search, GET searchable options

  Services/
    DocumentService.cs                Pipeline upload/parse/embed/persist; dopyty na úrovni dokumentu
    ChunkService.cs                   CRUD + filtrovanie jednotlivých chunkov
    SearchService.cs                  Filter + ranking cosine similarity
    AiHelper.cs                       Volania Claude pre topics/tags
    OnnxEmbeddingService.cs           Lokálny embedding + cosine similarity
    Interfaces/                       IDocumentService, IChunkService, ISearchService, IAiHelper, IEmbeddingService

  Parsers/
    DocxParser.cs                     .docx -> chunky (Open XML SDK)
    PdfParser.cs                      .pdf -> chunky (PdfPig + heuristiky veľkosti fontu/medzery riadkov)
    Interfaces/                       IDocxParser, IPdfParser

  Models/
    Chunk/Chunk.cs                    Entita Chunk (pozri Dátový model)
    Identity/ApplicationUser.cs       Používateľ ASP.NET Core Identity
    Requests/SearchRequest.cs
    Responses/SearchResponse.cs, SearchableOptionsResponse.cs

  DTOs/
    ChunkDto.cs                       Read model (API odpovede)
    CreateChunkDto.cs                 Výstup parsera / write model pred uložením

  Mappings/
    ChunkMappings.cs                  Konverzia Chunk <-> ChunkDto/CreateChunkDto

  Migrations/                         EF Core migrácie (schéma appky + schéma Identity)
  EmbeddingModel/                     Zabalený lokálny ONNX model (model.onnx, vocab.txt, NOTICE.txt)
  Properties/launchSettings.json      Lokálne run profily (porty, dev prostredie)
  appsettings.json, appsettings.Development.json

ContextEngine.Api.Tests/              xUnit testovací projekt
  Unit/                                Services/, Parsers/, Mappings/ - čisté unit testy (Moq fake objekty)
  Api/                                 In-process integračné testy (WebApplicationFactory<Program>)
  TestHelpers/                         FakeAiHelper, TestAuthHandler, TestDocuments, EmbeddingServiceFixture
```

## Testovanie

```bash
dotnet test ContextEngine.Api.sln
```

101 testov, rozdelených na dva druhy:

- **Unit testy** (`ContextEngine.Api.Tests/Unit/`) — services, parsery a mappings testované izolovane s mockovanými závislosťami (Moq). Pokrývajú `ChunkService`, `DocumentService`, `SearchService`, `OnnxEmbeddingService`, `DocxParser`, `PdfParser`, `ChunkMappings` a `GlobalExceptionHandler`.
- **API integračné testy** (`ContextEngine.Api.Tests/Api/`) — spúšťajú celú appku in-process cez `WebApplicationFactory<Program>` (pozri `ContextEngineApiFactory`), voči čerstvej dočasnej SQLite databáze pre každú testovaciu triedu, so skutočným `IAiHelper` nahradeným no-op `FakeAiHelper` (žiadne sieťové volania, na spustenie sady netreba API kľúč). Tieto testy defaultne aj obchádzajú autentifikáciu cez `TestAuthHandler` — falošnú schému, ktorá autentifikuje každý request ako fixného testovacieho používateľa — takže `ChunksControllerApiTests`, `DocumentsControllerApiTests` a `SearchControllerApiTests` sa môžu sústrediť čisto na business logiku namiesto plumbingu okolo tokenov.
  - `AuthenticationApiTests` je výnimka: nastaví `ContextEngineApiFactory.BypassAuthentication = false` a testuje *reálny* flow — `401` bez/s neplatným tokenom, `401` pri zlom hesle a kompletný cyklus register → login → autorizovaný request.

## Známe obmedzenia

Toto sú vedomé kompromisy pre fázu prototypu/lokálneho nástroja, nie chyby — sú tu uvedené, aby boli zámerným rozhodnutím na neskoršie prehodnotenie, nie prekvapením:

- **Filtrovanie `Topics`/`Tags`/dátumového rozsahu načíta celú tabuľku `Chunks` do pamäte** (pozri [Dátový model](#dátový-model)). V poriadku pri stovkách/nízkych tisíckach chunkov; prvá vec na opravu (normalizovať `Topics`/`Tags` do vlastných join tabuliek), ak dataset výrazne narastie.
- **Žiadna automatická migrácia pri štarte.** `dotnet ef database update` treba spustiť ručne po každom pulle novej migrácie. Zámer — auto-migrácia v `Program.cs` je bežná pasca pri nasadeniach s viacerými inštanciami — ale pre tento single-instance use case sa oplatí zautomatizovať v deploy skripte.
- **`POST /api/documents` prijíma server-lokálnu cestu k súboru**, nie multipart upload. Toto je zámer pre lokálny nástroj, kde volajúci (napr. AI agent) a API zdieľajú súborový systém, ale znamená to, že volajúci môže prinútiť server prečítať *ktorýkoľvek* súbor, na ktorý má OS-úrovňové oprávnenie — nevystavuj tento endpoint mimo dôveryhodnej lokálnej/súkromnej siete bez pridania validácie cesty.
- **Plochá autorizácia**: ktorýkoľvek prihlásený používateľ môže čítať/zapisovať/mazať ktorýkoľvek chunk alebo dokument — neexistuje vlastníctvo dát per-user. V poriadku pre nástroj s jedným operátorom; pre skutočne multi-tenant nasadenie by bolo treba stĺpec s vlastniacim používateľom + autorizačné kontroly.
- **Tabuľky sa štrukturálne neparsujú.** Chunk typu `Table` obsahuje text celej tabuľky ako jeden blob (`InnerText`), nie jednotlivé riadky/bunky — `ChunkType.TableRow`/`TableCell` v enume existujú, ale zatiaľ ich nič neprodukuje.
- **Strom `Chunk.Parent`/`Children` dnes žiadny z parserov nepoužíva** — každý chunk z volania `POST /api/documents` je súrodenec s `ParentId == null`. Self-referencing schéma je pripravená na vnáranie (napr. odseky pod svoj nadpis), akonáhle to nejaký parser implementuje.

## Poznámka k licencii

Zabalený embedding model (`ContextEngine.Api/EmbeddingModel/model.onnx`, `vocab.txt`) je nezmenená redistribúcia z [Xenova/all-MiniLM-L6-v2](https://huggingface.co/Xenova/all-MiniLM-L6-v2) (čo je ONNX export z [sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)) pod licenciou Apache License 2.0 — plnú atribúciu nájdeš v [`EmbeddingModel/NOTICE.txt`](ContextEngine.Api/EmbeddingModel/NOTICE.txt).
