# Document Assistant

A simple AI knowledge base (RAG). Users register and log in, upload documents (PDF, DOCX, MD, TXT), and ask questions that are answered from their own documents, with citations. This is a portfolio project, so **simplicity and clarity beat feature count**.

Accounts/auth (register, login, `/api/me`) and document management (upload, list, download, delete)
exist today. Semantic search and Q&A are not built yet.

## Docs: use Context7, not memory

Angular 22, .NET 10, EF Core 10, and ASP.NET Core Identity are newer than most training data. Before writing code against any of them, look them up with the Context7 MCP server: `resolve-library-id`, then `query-docs` with the returned `/org/project` ID — one concept per call. Skip it for this repo's own logic.

## Layout

Two independent apps, no root package manager or solution file: `client/` (Angular 22, standalone components, TypeScript 6, Tailwind v4) and `server/` (ASP.NET Core minimal API, `net10.0`, EF Core + Npgsql/PostgreSQL, ASP.NET Core Identity, JWT bearer auth).

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
dotnet ef database update          # apply migrations (the app never migrates on boot)
``
The root `.vscode/tasks.json` has `Run Full Project` to start both in parallel.

## Configuration & secrets

**Server.** `Program.cs` loads `server/.env` (gitignored; template is `server/.env.example`)
through DotNetEnv's `NoClobber()` _before_ the builder is created, walking up from the current
directory to find `server/server.csproj`. Required keys: `ConnectionStrings__DefaultConnection`,
`Jwt__Key` (≥32 bytes), `Jwt__Issuer`, `Jwt__Audience`, `Client__Origin`,
`Azure__BlobStorage__ConnectionString`, `Azure__BlobStorage__ContainerName`, `ASPNETCORE_URLS`.
`AddAllServices` throws `InvalidOperationException` at startup when any is missing or too short, so
a missing `.env` crashes `dotnet run` instead of starting a server. `appsettings.json` carries only
non-secret defaults (Jwt `Issuer`/`Audience`/`ExpirationMinutes`, itself 60).

**Client.** The API base URL is `client/src/environments/environment.ts`, a default export
`{ API_URL }` — import it as `import env from '../environments/environment'`. There is no
`injection token`; components read `env.API_URL` directly. `client/public/runtime-config.json`
exists but is gitignored and referenced nowhere.

**Coupling.** Changing host/port/origin means editing three places: the server port in
`server/Properties/launchSettings.json`, the allowed browser origin in `Client__Origin` (default
`http://localhost:4200`), and the client base URL in `environment.ts`.

## Server architecture

`Program.cs` is now a thin bootstrap: load `.env` → `AddAllServices` → `UseCors` /
`UseAuthentication` / `UseAuthorization` → `MapGet("/")`, `MapEndpoints()`, and the `/health`
JSON writer. Routes are **not** declared there anymore.

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
  while upload/list share the `DocumentResponse` record in `Common/Documents/`.
- **`Services/DocumentStorage.cs`** — singleton wrapping one private Azure blob container
  (`Azure:BlobStorage:ConnectionString` / `:ContainerName`; Azurite emulates it locally). Exposes
  `UploadAsync` / `OpenReadAsync` / `DeleteAsync` and creates the container lazily, once. Blobs are
  named `{userId}/{documentId}{extension}` (never the client-supplied name), and the original file
  name and content type live in the `Documents` Postgres row — that is what download serves back.
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
- **`Data/ApplicationDbContext.cs`** — `IdentityDbContext<IdentityUser>`; migrations live in
  `Data/Migrations/`. Nothing calls `Database.Migrate()`, so apply schema with
  `dotnet ef database update` before running.
- **Auth contract.** Bad login → `401`. Invalid register input → `ValidationProblem` (`400` with an
  `errors` dictionary). Tokens validate with `NameClaimType = ClaimTypes.NameIdentifier`, mapped
  inbound claims, and a 30 s clock skew.
- **`/health`** wraps the built-in health-check middleware and returns **503 when unhealthy**, so
  the client sees a failed request, never a readable `"Unhealthy"` body.

## Client architecture

Bootstrapped from `main.ts` with `appConfig`; no `AppModule`. App-wide providers go in
`src/app/app.config.ts`: `provideBrowserGlobalErrorListeners()`, `provideRouter(routes)`, and
`provideHttpClient(withFetch(), withInterceptors([authInterceptor]))`.

- `app.routes.ts`: `/` is a componentless public home behind `homeGuard` (guests stay, signed-in
  users go to `/my-documents`); `/my-documents` is a componentless shell behind `authGuard` (guests
  go to `/auth`) whose default child renders `DocumentsPage`; `/auth` renders `AuthPage` behind
  `guestGuard` (signed-in users go to `/my-documents`). All three guards live in
  `auth/auth.guards.ts` and redirect on the session. The `App` shell shows Login / Register links
  (`/auth`, the latter with `?mode=register`) to guests and the signed-in email plus a sign-out
  button otherwise; signing out clears the session and navigates to `/`, which guests can view.
- `auth/auth.service.ts` (`providedIn: 'root'`) holds the `AuthSession` in a signal and mirrors it
  to `localStorage` under `document-assistant.auth`; it validates shape and expiry on restore and
  clears an expired session. Exposes `register` / `login` / `getCurrentUser` / `logout`.
- `auth/auth.interceptor.ts` attaches `Authorization: Bearer <token>` to requests whose URL starts
  with `${env.API_URL}/api/`.
- `auth/auth-page.ts` + `auth-page.html` — one component toggling login/register modes with
  reactive forms, seeded from the `?mode=register` query param; it flattens server ProblemDetails
  `errors`/`detail` into a single error message and navigates to `/my-documents` after a successful
  login or register.
- `documents/documents.service.ts` (`providedIn: 'root'`) — the list lives in signals
  (`documents`, `isLoading`, `loadError`) and it exposes `reload` / `upload` / `delete` /
  `download`. `download` requests `responseType: 'blob'`, so an error body arrives as a `Blob`
  rather than parseable JSON.
- `documents/documents-page.ts` + `documents-page.html` — the upload control, the list, an inline
  delete confirmation (arm, then Confirm/Cancel), and a Download button that saves the blob under its
  original file name through a transient object URL.
- Prefer the signal-based `httpResource` where a GET fits (nothing uses it yet); the documents list
  and auth flows use `HttpClient` + RxJS because a resource registers a pending task that stops
  `fixture.whenStable()` resolving in the existing specs, and auth is imperative anyway.
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
`HTMLAnchorElement.prototype.click`, restoring both afterwards.

## Git

Root `.gitignore` is the only one — the generated `client/.gitignore` does not exist, though a
comment in the root file still claims it does. It excludes `.env`, `client/public/runtime-config.json`,
`.vscode/`, and `.claude/settings.local.json`.
```
