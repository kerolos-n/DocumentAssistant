# Document Assistant

A simple AI knowledge base (RAG). Users register and log in, upload documents (PDF, DOCX, MD, TXT), and ask questions that are answered from their own documents, with citations. This is a portfolio project, so **simplicity and clarity beat feature count**.

Accounts/auth (register, login, `/api/me`), document management (upload, list, download, delete),
the ingest half of the RAG pipeline — extract → chunk → embed → store into `DocumentChunks` — and
grounded Q&A (`POST /api/questions`: embed the question → retrieve the nearest chunks → generate an
answer with citations) all exist today.

## Docs: use Context7, not memory

Angular 22, .NET 10, EF Core 10, ASP.NET Core Identity, pgvector, and Google's Gemini embedding and
chat APIs are newer than most training data. Before writing code against any of them, look them up with
the Context7 MCP server: `resolve-library-id`, then `query-docs` with the returned `/org/project`
ID — one concept per call. Skip it for this repo's own logic.

## Layout

Two independent apps, no root package manager or solution file: `client/` (Angular 22, standalone components, TypeScript 6, Tailwind v4) and `server/` (ASP.NET Core minimal API, `net10.0`, EF Core + Npgsql/PostgreSQL with pgvector, ASP.NET Core Identity, JWT bearer auth).

## Commands

```bash
# client/ — dev server :4200
npm install
npm start                    # ng serve
npm run build                # ng build
npm run watch                # ng build --watch (development)
npm test -- --watch=false    # Vitest (jsdom); watch is the default in a TTY
npm test -- --filter "^App"                    # single suite/test by regex
npm test -- --include src/app/app.spec.ts      # single file
npx prettier --write .       # single quotes, printWidth 100, angular parser for HTML

# server/ — http://localhost:3000
dotnet watch                 # hot reload
dotnet run                   # once (uses the `http` profile)
dotnet build
dotnet ef migrations add <Name>    # EF Core; needs the .env connection string
dotnet ef database update          # apply migrations by hand; normally unnecessary, because
                                   # startup applies them (the migration runs CREATE EXTENSION
                                   # vector, so the Postgres server must ship pgvector — e.g.
                                   # the pgvector/pgvector image)
```
The root `.vscode/tasks.json` has `Run Full Project` to start both in parallel.

## Configuration & secrets

**Server.** `Program.cs` loads `server/.env` (gitignored; template is `server/.env.example`)
through DotNetEnv's `NoClobber()` _before_ the builder is created, walking up from the current
directory to find `server/server.csproj`. Required keys: `ConnectionStrings__DefaultConnection`,
`Jwt__Key` (≥32 bytes), `Jwt__Issuer`, `Jwt__Audience`, `Client__Origin`,
`Azure__BlobStorage__ConnectionString`, `Azure__BlobStorage__ContainerName`, `Gemini__ApiKey`,
`ASPNETCORE_URLS`.
`AddAllServices` throws `InvalidOperationException` at startup when any is missing or too short, so
a missing `.env` crashes `dotnet run` instead of starting a server. `Gemini__ApiKey` is required
because chunk embedding is not optional: without it every upload would fail, so the app refuses to
start rather than accept documents it cannot index. `appsettings.json` carries only
non-secret defaults (Jwt `Issuer`/`Audience`/`ExpirationMinutes`, itself 60; `Ingestion`
chunk size/overlap/batch size; `Retrieval` top-K (5) and similarity threshold (0.5 — a floor for
"obviously unrelated", not a relevance test, because the 1536-dimension vectors are truncated from
the model's 3072 and truncation deflates cosine scores); `RateLimiting` per-policy permit count and
window seconds; Gemini `EmbeddingModel`, `ChatModel`, `ChatMaxOutputTokens`, and
`ChatTimeoutSeconds`).

**Client.** The API base URL is `client/src/environments/environment.ts`, a default export
`{ API_URL }` — import it as `import env from '../environments/environment'`. There is no
`injection token`; components read `env.API_URL` directly. `client/public/runtime-config.json`
exists but is gitignored and referenced nowhere.

**Coupling.** Changing host/port/origin means editing three places: the server port in
`server/Properties/launchSettings.json`, the allowed browser origin in `Client__Origin` (default
`http://localhost:4200`), and the client base URL in `environment.ts`.

## Server architecture

`Program.cs` is now a thin bootstrap: load `.env` → `AddAllServices` →
`ApplyMigrationsAsync` → `CorrelationIdMiddleware` → `UseExceptionHandler` → `UseCors` /
`UseAuthentication` /
`UseRateLimiter` / `UseAuthorization` → `MapGet("/")`, `MapEndpoints()`, and the `/health`
JSON writer. Routes are **not** declared there anymore. The rate limiter sits _after_
authentication on purpose: its per-user partition reads the `NameIdentifier` claim, which is only
populated once the bearer token is validated.

- **`DependencyInjection.cs`** — `AddAllServices` is the composition root: DbContext, Identity,
  CQRS handler registrations, endpoint discovery, JWT bearer auth, CORS. Note it calls
  `AddEndpoints(...)` twice (harmless — `TryAddEnumerable` dedupes — but redundant).
- **Vertical slices** under `Features/<Area>/<UseCase>.cs`. Each file holds a `Command`/`Query`
  record, a `Handler`, and a public `sealed class Endpoint : IEndpoint`. Auth slices:
  `POST /api/auth/register`, `POST /api/auth/login`, and `GET /api/me` (`RequireAuthorization()`).
- **Document slices** under `Features/Documents/`: `POST /api/documents` (upload — `IFormFile`, a
  20 MB cap, and a `.pdf`/`.docx`/`.md`/`.txt` extension allow-list, so it calls
  `DisableAntiforgery()`), `GET /api/documents` (list, newest first),
  `GET /api/documents/{id:guid}/download`, and `DELETE /api/documents/{id:guid}`. All
  `RequireAuthorization()`, and every handler is scoped by the `NameIdentifier` claim taken from the
  token rather than the URL — another user's document is indistinguishable from a missing one
  (`404`). Download returns `Results.File(stream, contentType, fileName)`, which sets
  `Content-Disposition`; its response record is nested in the slice (`DocumentDownloadResponse`),
  while upload/list share the `DocumentResponse` record in `Common/Documents/` — which now carries
  `Status` and `ErrorMessage` alongside the file metadata. Upload persists the row as `Pending` and
  only then enqueues it for ingestion; delete needs no explicit chunk cleanup because the
  `DocumentChunks` foreign key cascades. Upload also enforces a **per-user document cap**
  (`MaxDocumentsPerUser = 50`) by counting rows _before_ the blob write — an over-limit upload must
  never create an orphan — and throws a `ValidationException` that becomes a `400` with a `file`
  error. If the metadata `SaveChangesAsync` fails after the blob was written, the handler deletes
  the blob best-effort (`TryDeleteBlobAsync`) so no unreferenced object is stranded.
- **`Services/DocumentStorage.cs`** — singleton wrapping one private Azure blob container
  (`Azure:BlobStorage:ConnectionString` / `:ContainerName`; Azurite emulates it locally). Exposes
  `UploadAsync` / `OpenReadAsync` / `DeleteAsync` and creates the container lazily, once. Blobs are
  named `{userId}/{documentId}{extension}` (never the client-supplied name), and the original file
  name and content type live in the `Documents` Postgres row — that is what download serves back.
- **Ingestion pipeline** under `Services/Ingestion/`, run by `DocumentIngestionWorker` (a
  `BackgroundService`): extract → chunk → embed → store. Uploads and processing are decoupled by
  `DocumentIngestionQueue`, a singleton wrapping an unbounded `Channel<Guid>`; the upload endpoint
  enqueues an id _after_ `SaveChangesAsync` commits, because the worker looks the document up by
  id. The worker drains serially, moves `Pending` → `Processing` → `Ready`/`Failed`, and wraps each
  document in its own try/catch so one bad file cannot kill it. On startup it re-queues rows stuck
  in `Pending`/`Processing` (it runs before the read loop, so a crash mid-ingest self-heals). Only a
  `DocumentIngestionException`'s message reaches the user's `ErrorMessage`; everything else is
  logged and reported generically. Re-processing deletes the document's existing chunks first, so a
  retry replaces rather than duplicates.
- **Text extractors** implement `ITextExtractor` (`bool CanHandle(extension)` + `ExtractAsync`
  returning `TextSegment(Text, PageNumber)`); `TextExtractorResolver` picks one from the registered
  set by file extension, so adding a format is adding a class. `PdfTextExtractor` (PdfPig) yields
  one segment per page and carries the page number; `DocxTextExtractor`
  (DocumentFormat.OpenXml) flattens paragraphs and has no pages; `PlainTextExtractor` handles MD
  and TXT as UTF-8. The PDF and DOCX readers are synchronous, so both run inside `Task.Run`. A
  PDF with no text layer (a scan) yields no segments and becomes `Failed` with a message naming
  OCR — OCR itself is out of scope.
- **`TextChunker`** splits by size with overlap, preferring paragraph → sentence → space
  boundaries, and chunking each segment separately so a chunk's page number is never ambiguous.
  Size/overlap/batch size come from the `Ingestion` config section, defaulting to
  `Common/Ingestion/IngestionDefaults.cs`.
- **`IEmbeddingService` / `GeminiEmbeddingService`** — calls `batchEmbedContents` on the
  Generative Language REST API directly rather than through an SDK, so there is no package version
  to keep in step with the framework. Two details matter: it asks for
  `outputDimensionality: 1536` (a width `gemini-embedding-001` supports, which is why the column is
  1536 with no migration churn) and it L2-normalises every vector, because Gemini only returns
  normalised output at full 3072 width. The interface splits the two task types —
  `EmbedDocumentsAsync` uses `RETRIEVAL_DOCUMENT`, `EmbedQueryAsync` uses `RETRIEVAL_QUERY` — because
  mixing them degrades similarity. Batches are capped at 100 (the API's limit), and provider error
  bodies are logged rather than surfaced; the user sees only the status code. The body is logged
  **only in Development** (the service takes `IHostEnvironment`), because a provider error can echo
  the chunk text it rejected — production logs carry the status code alone.
- **Question slice** — `Features/Questions/AskQuestion.cs` is a *command* slice
  (`POST /api/questions`), so `ValidationBehavior` runs its `Validator`; the earlier `IQuery` slices
  validate inline instead. The handler, in order: if the user has no `Ready` document, return
  `NoDocuments` without embedding or calling the model; embed the question as a query; take the
  nearest `TopK` chunks with `chunk.Embedding.CosineDistance(queryVector)` (pgvector `<=>`, via
  `Pgvector.EntityFrameworkCore`), joined to `Ready` documents and filtered by `UserId` on *both*
  chunk and document so one user can never surface another's text; drop anything below the
  similarity threshold and, if nothing survives, return `NoRelevantContext` — the "I don't know"
  path, which skips the model entirely; otherwise answer and cite the chunks used. Similarity is
  `1 − distance`. All three non-answer outcomes keep `200` and set `isAnswerable: false`, with
  `outcome` telling the client which one it is. Citation snippets are whitespace-collapsed and cut
  to a word boundary (`QuestionPrompt.BuildSnippet`). Retrieval knobs come from the `Retrieval`
  section (`Common/Questions/RetrievalOptions.cs`).
- **`IChatService` / `GeminiChatService`** — `Services/Chat/`, hand-rolled `generateContent` calls
  (like the embedder, no SDK), configured with a `systemInstruction`, low temperature, a
  `maxOutputTokens` cap, and its own `HttpClient` timeout. A provider error, a timeout, or an empty
  candidate becomes a `ChatServiceException`, which `GlobalExceptionHandler` maps to **502** — the
  client's error state. The message is user-safe; provider error bodies are logged only in
  Development. `QuestionPrompt`
  (`Features/Questions/`) owns the system prompt — answer only from context, admit ignorance, and
  treat document text as data, never instructions — so the injection guard lives in one place.
- **`Common/Endpoints/IEndpoint.cs`** — implement `void MapEndpoint(IEndpointRouteBuilder)`.
  `Extensions/EndpointExtensions.cs` reflects over the assembly at startup, registers every
  implementation as a transient, and `MapEndpoints()` invokes them. Adding an endpoint = adding a
  class; no edit to `Program.cs`.
- **`Common/CQRS/`** — `ICommand`, `ICommand<TResponse>`, `ICommandHandler<>`/`ICommandHandler<,>`,
  `IQuery<TResponse>`, `IQueryHandler<,>`, all in namespace `DocumentAssistant.Common.CQRS`.
  Handlers are auto-scanned by Scrutor with a scoped lifetime, so a new slice needs no
  `DependencyInjection.cs` edit; command handlers are wrapped by `ValidationBehavior`, and
  `AddValidatorsFromAssembly(..., includeInternalTypes: true)` is what makes the slices' internal
  `Validator` classes run.
- **`Services/JwtTokenService.cs`** — singleton; HMAC-SHA256, enforces the 32-byte key. Emits
  `sub`/`email`/`jti` claims and returns `TokenResponse` (`Common/Auth/`) —
  `{ AccessToken, ExpiresAtUtc, UserId, Email }`, serialized camelCase.
- **`Data/ApplicationDbContext.cs`** — `IdentityDbContext<IdentityUser>` over `Documents` and
  `DocumentChunks`; migrations live in `Data/Migrations/`. `Program.cs` calls
  `ApplyMigrationsAsync` (`Extensions/MigrationExtensions.cs`) right after `builder.Build()` and
  before `app.Run()`, so pending migrations land before the host starts its hosted services — that
  ordering is what keeps the ingestion worker's startup re-queue from querying a missing table. A
  migration failure propagates and the app refuses to start. It declares the `vector` extension
  and maps `DocumentChunk.Embedding` as a `vector(1536)` column, so `AddDbContext` must keep
  `npgsql => npgsql.UseVector()` — without it Npgsql cannot read the type and inserts fail at
  runtime, not at startup. `Document.Status` is a `DocumentStatus` enum stored as text with a
  `'Pending'` SQL default, which is what backfills rows that predate the column (they then get
  re-ingested on the next start). There is deliberately **no HNSW/IVFFlat index yet** — add one to
  the `DocumentChunks` config once data volume justifies it.
- **Auth contract.** Bad login → `401`. Invalid register input → `ValidationProblem` (`400` with an
  `errors` dictionary). Tokens validate with `NameClaimType = ClaimTypes.NameIdentifier`, mapped
  inbound claims, and a 30 s clock skew.
- **`/health`** wraps the built-in health-check middleware and returns **503 when unhealthy**, so
  the client sees a failed request, never a readable `"Unhealthy"` body.
- **Protection & hardening.** Two named rate-limit policies live in
  `Common/RateLimiting/RateLimitPolicies.cs` and are registered in `DependencyInjection.cs`
  (`AddRateLimiting`, configured from the `RateLimiting` section): `questions` (default 20 / 5 min)
  applied to `POST /api/questions`, and `uploads` (default 20 / hour) on `POST /api/documents`. Each
  is a fixed window partitioned by the `NameIdentifier` claim, falling back to the client IP for
  anonymous callers. Rejections answer **429** with a ProblemDetails body and a `Retry-After` header
  via `OnRejected`. The question length cap (`AskQuestion.MaxQuestionLength = 2000`) is enforced by
  the slice's `Validator`; the per-user document cap is enforced in the upload handler (see above).
  `Common/Logging/CorrelationIdMiddleware.cs` runs first: it assigns every request a correlation id
  (from `X-Correlation-Id` when it is a safe token, else a fresh GUID), puts it in `TraceIdentifier`
  and a log scope, echoes it in the response header, and logs method/path/status/elapsed. The
  ingestion worker logs chunk count and elapsed ms per document. `GlobalExceptionHandler` echoes the
  correlation id as `traceId` on every ProblemDetails and keeps `500` details generic, so stack
  traces stay in the logs and never reach a client; the provider services never log request content
  in production.

## Client architecture

Bootstrapped from `main.ts` with `appConfig`; no `AppModule`. App-wide providers go in
`src/app/app.config.ts`: `provideBrowserGlobalErrorListeners()`, `provideRouter(routes)`, and
`provideHttpClient(withFetch(), withInterceptors([authInterceptor, sessionExpiryInterceptor]))`.

- `app.routes.ts`: `/` renders `HomePage` (the public landing page) behind `homeGuard` (guests stay,
  signed-in users go to `/ask`); `/my-documents` is a componentless shell behind `authGuard` (guests
  go to `/auth`) whose default child renders `DocumentsPage`; `/ask` renders `AskPage` behind
  `authGuard`; `/auth` renders `AuthPage` behind
  `guestGuard` (signed-in users go to `/ask`). All three guards live in
  `auth/auth.guards.ts` and redirect on the session. The `App` shell is a sticky, responsive navbar:
  a brand link on the left, account links inline from the `md` breakpoint up, and a hamburger button
  that expands a mobile panel (state in a `menuOpen` signal; the panel unmounts when closed, so the
  default DOM has one copy of each link). Guests get Login / Register (`/auth`, the latter with
  `?mode=register`); signed-in users get Ask / My documents, their email, and a sign-out button;
  signing out clears the session and navigates to `/`, which guests can view.
- `home/home-page.ts` + `home-page.html` — the signed-out landing page: a hero, a three-step
  upload/index/ask explainer, and call-to-action buttons to `/auth` and `/auth?mode=register`. No
  HTTP calls, so its spec needs no flush.
- `auth/auth.service.ts` (`providedIn: 'root'`) holds the `AuthSession` in a signal and mirrors it
  to `localStorage` under `document-assistant.auth`; it validates shape and expiry on restore and
  clears an expired session. Exposes `register` / `login` / `getCurrentUser` / `logout`.
- `auth/auth.interceptor.ts` attaches `Authorization: Bearer <token>` to requests whose URL starts
  with `${env.API_URL}/api/`.
- `auth/session-expiry.interceptor.ts` handles **401 globally**: a rejected token means the stored
  session is stale, so it logs out and navigates to `/auth`. Requests to `/api/auth/*` are excluded —
  a rejected login is bad credentials, not an expired session. `shared/http-error.ts` centralises the
  status→message mapping (`0` offline, `401`/`403`/`404`/`429`/`500`, `502`/`504` assistant down) that
  the pages use, so every screen explains a failure the same way; `429` renders the "try again in a
  moment" message.
- `auth/auth-page.ts` + `auth-page.html` — one component toggling login/register modes with
  reactive forms, seeded from the `?mode=register` query param; it flattens server ProblemDetails
  `errors`/`detail` into a single error message and navigates to `/ask` after a successful
  login or register.
- `documents/documents.service.ts` (`providedIn: 'root'`) — the list lives in signals
  (`documents`, `isLoading`, `loadError`) and it exposes `reload` / `upload` / `delete` /
  `download`. `download` requests `responseType: 'blob'`, so an error body arrives as a `Blob`
  rather than parseable JSON. `DocumentSummary` mirrors the server's `DocumentStatus` as a string
  union. Ingestion progress is **polled, not pushed**: a `computed` boolean
  (`hasUnsettledDocuments`) drives an `effect` that subscribes to an `interval` only while some
  document is `Pending`/`Processing`, and the effect's `onCleanup` tears the subscription down once
  everything settles. Reading the computed rather than the array is what keeps the cadence steady —
  the effect re-runs only when that boolean flips, not on every response. Polls are silent and
  swallow errors: a refresh must not flip `isLoading` (that would replace the list with a loading
  message every few seconds), and one blip must not kill the cadence.
- `documents/documents-page.ts` + `documents-page.html` — the upload control, the list, an inline
  delete confirmation (arm, then Confirm/Cancel), and a Download button that saves the blob under its
  original file name through a transient object URL. Each row carries a status badge whose colours
  come from a `Record<DocumentStatus, string>` of _complete_ Tailwind class names (complete, not
  fragments, so Tailwind's scanner finds them in the `.ts` file); `Processing` also renders an
  `animate-spin` spinner, and `Failed` shows the server's reason inline and as a `title` tooltip.
- `questions/questions.service.ts` (`providedIn: 'root'`) + `questions/ask-page.ts`/`.html` — a
  two-column layout that stacks on small screens and goes side-by-side from the `lg` breakpoint: a
  narrow (sticky) left sidebar listing the user's documents (name, status dot, plus a "Manage
  documents" link to `/my-documents`) and the chat on the right. The sidebar injects the shared
  `DocumentsService` and reads its signals, so it inherits the same polled list — no second fetch
  loop. The chat is one POST to `/api/questions` returning
  `{ isAnswerable, outcome, answer, citations }`, with `outcome`
  (`Answered` / `NoDocuments` / `NoRelevantContext`) driving three distinct panels: the answer with
  its citations, an amber "no documents" card linking to `/my-documents`, and a neutral "no answer
  found" card. Submit is disabled while the input is blank, too long, or a request is in flight, and a
  `502` (the assistant is down or timed out) renders the error alert. The form enforces the server's
  2000-character cap (`maxlength` plus a `canSubmit` upper bound and a live counter), and a `429`
  renders the shared rate-limit message. The chat itself makes no request on
  load, but the sidebar's `DocumentsService` fires a `GET /api/documents`, so its spec flushes that
  before asserting.
- Prefer the signal-based `httpResource` where a GET fits (nothing uses it yet); the documents list
  and auth flows use `HttpClient` + RxJS because a resource registers a pending task that stops
  `fixture.whenStable()` resolving in the existing specs, and auth is imperative anyway. Polling
  reinforces that choice: the list needs `interval` + `switchMap`, which a resource cannot express.
- Tailwind v4 is configured in CSS (`@import 'tailwindcss'` in `src/styles.css`, PostCSS bridge in
  `.postcssrc.json`); there is no `tailwind.config.js`.

## Tests

Colocated `*.spec.ts`, run by Vitest via `@angular/build:unit-test` in jsdom. Any component using
`httpResource`/`HttpClient` needs both `provideHttpClient()` and `provideHttpClientTesting()` in the
`TestBed` providers or the suite won't compile; `App` also needs `provideRouter(routes)`, and the
auth specs need `provideHttpClient(withInterceptors([authInterceptor]))`. The auth specs build URLs
from `env.API_URL` directly; it is a plain string read at module scope, so providing it as a DI
token has no effect. The `client/.vscode/launch.json` `ng test`
entry still points at the old Karma debug URL (`:9876`) and does not apply. The documents download
spec stubs `URL.createObjectURL` / `revokeObjectURL` (jsdom implements neither) and spies
`HTMLAnchorElement.prototype.click`, restoring both afterwards. The documents service only starts
its polling interval when an unsettled document is present, so the documents spec's
`sampleDocument()` defaults to `Ready` and every other spec never starts a timer — which is what
keeps `fixture.whenStable()` and `httpTesting.verify()` meaningful. The polling spec installs
`vi.useFakeTimers()` _before_ `TestBed.createComponent`, because the interval is created from the
service constructor; faking the clock after the component exists would leave the real timer
running. `auth/session-expiry.interceptor.spec.ts` covers the global 401 path (protected call →
logout + redirect, login → untouched); the ask-page and documents-page specs cover the `429` message
and the question-length cap.

## Git

Root `.gitignore` is the only one — the generated `client/.gitignore` does not exist, though a
comment in the root file still claims it does. It excludes `.env`, `client/public/runtime-config.json`,
`.vscode/`, and `.claude/settings.local.json`.
