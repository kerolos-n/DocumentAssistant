# Document Assistant

A simple AI knowledge base (RAG). Users register and log in, upload documents (PDF, DOCX, MD, TXT), and ask questions that are answered from their own documents, with citations. This is a portfolio project, so **simplicity and clarity beat feature count**.

## Docs: use Context7, not memory

Angular 22 and .NET 10 are newer than most training data. Before writing code against either
framework's APIs, look them up with the Context7 MCP server: `resolve-library-id`, then `query-docs`
with the returned `/org/project` ID — one concept per call. Skip it for this repo's own logic.

## Layout

Two independent apps, no root package manager or solution file: `client/` (Angular 22, standalone
components, TypeScript 6, Tailwind v4) and `server/` (ASP.NET Core minimal API, `net10.0`).

## Commands

```bash
# client/ — dev server :4200
npm install
npm start                    # ng serve
npm run build                # ng build
npm test -- --watch=false    # Vitest (jsdom); watch is the default in a TTY
npm test -- --filter "^App"                    # single suite/test by regex
npm test -- --include src/app/app.spec.ts      # single file
npx prettier --write .       # single quotes, printWidth 100, angular parser for HTML

# server/ — http://localhost:3000
dotnet watch                 # hot reload
dotnet run                   # once (uses the `http` profile)
dotnet build
```

The root `.vscode/tasks.json` has `Run Full Project` to start both in parallel.

## Architecture

**Client ↔ server coupling.** The client calls the server at an absolute URL hardcoded in
`client/src/app/app.ts` (`healthUrl`) — there is no `environment.ts`. The port lives in
`server/Properties/launchSettings.json`, and the CORS origin in `server/Program.cs` (policy
`client`, allowing only `http://localhost:4200`). Changing the server port or client origin means
editing all three.

**Server.** `Program.cs` is the entire API — top-level statements, no controllers or `Startup`
class; add routes there with `app.MapGet`/`MapPost`. `GET /health` wraps the built-in health-check
middleware (`AddHealthChecks` + `MapHealthChecks`) with a JSON `ResponseWriter`. Note it returns
**503 when unhealthy**, so the client sees a failed request, never a readable `"Unhealthy"` body.

**Client.** Bootstrapped from `main.ts` with `appConfig`; no `AppModule`. App-wide providers go in
`src/app/app.config.ts` (`provideHttpClient(withFetch())`, `provideRouter`). Fetch data with the
signal-based `httpResource` from `@angular/common/http` — expose `isLoading()`/`error()`/`value()`
through a `computed()` — rather than manual `HttpClient.subscribe`. `app.routes.ts` is empty.
Tailwind v4 is configured in CSS (`@import 'tailwindcss'` in `src/styles.css`, PostCSS bridge in
`.postcssrc.json`); there is no `tailwind.config.js`.

**Tests.** Colocated `*.spec.ts`, run by Vitest via `@angular/build:unit-test` in jsdom. Any
component using `httpResource`/`HttpClient` needs both `provideHttpClient()` and
`provideHttpClientTesting()` in the `TestBed` providers or the suite won't compile. The
`client/.vscode/launch.json` `ng test` entry still points at the old Karma debug URL (`:9876`) and
does not apply.

**Git.** `.gitignore` exists only at the repo root — the generated `client/.gitignore` was removed,
so root patterns are what exclude `client/node_modules`, `client/dist`, and `client/.angular`.
