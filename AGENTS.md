# AGENTS.md

## Project Overview

OpenMedia (internally **Serata**) is an Emby-compatible media server.

- Backend: C# on .NET 10, ASP.NET Core Kestrel, PostgreSQL via Npgsql.
- Frontend: Vue 3 + TypeScript + Vite, in `web/`.
- API contract: Emby-compatible DTO/JSON shapes; `/emby` is the default prefix.
- Entry point: `MediaBrowser.Server/MediaBrowser.Server.csproj`.
- Solution: `MediaBrowser.sln`.

Read the nearest `AGENTS.md` first. For this repository, the source of truth is:

1. Current code and `MediaBrowser.sln`.
2. `.github/workflows/ci.yml` and the build/test scripts.
3. `README.md`, `web/README.md`, and `tests/README.md`.
4. `openapi.json` plus the request/response types and behavior implemented in
   current code.

Treat deleted documentation and historical drafts as background only.

## Setup Commands

```powershell
# Backend
dotnet restore MediaBrowser.sln
dotnet build MediaBrowser.sln -c Debug --nologo

# Frontend
cd web
pnpm install
pnpm dev

# Local PostgreSQL
# Host=localhost;Port=5432;Database=openmedia;Username=postgres;Password=123456
```

Run the backend with:

```powershell
dotnet run --project MediaBrowser.Server/MediaBrowser.Server.csproj -c Debug
```

Use `http://localhost:8096` for the backend. For the web client, use
`http://localhost:5173` (not `127.0.0.1`; the dev server is IPv6-only).

Do not commit local secrets, real Emby credentials, server URLs, `ProgramData-*`,
`web/dist`, `web/.cdp-profile*`, probe screenshots, or generated perf results
unless the task explicitly requires a fixture/report update.

## Build and Test Commands

Before finishing a code change, run the checks relevant to what changed.

```powershell
# Backend build (matches CI)
dotnet build MediaBrowser.sln -c Release --nologo

# Backend tests; PostgreSQL integration tests require a running PG
dotnet test MediaBrowser.Tests/MediaBrowser.Tests.csproj -c Release --nologo

# Frontend checks (matches CI)
cd web
pnpm install --frozen-lockfile
pnpm type-check
pnpm build-only
```

Useful focused checks:

```powershell
dotnet test MediaBrowser.Tests -c Release --filter "FullyQualifiedName~PgData"
dotnet test MediaBrowser.Tests -c Release --filter "FullyQualifiedName~ActivityLogTypeCacheTests"
cd web; node tools/probe-libs.mjs
```

Full API/scenario regression is in `tests/README.md`; run it only against a
started server and the documented fixtures. `perf-smoke` in CI is informational
and has no hard threshold. Do not claim a performance target passed unless you
ran the matching Release test and recorded the environment/scale.

PostgreSQL integration tests create isolated schemas and must not modify the
shared `openmedia` data. Use `OPENMEDIA_TEST_PGCONN` to point tests at a test
database when needed. Never put synthetic perf data into the shared library.

## Architecture and Boundaries

- `MediaBrowser.Api/` contains REST request DTOs and service classes; keep it
  protocol-focused.
- `src/Emby.Server.PgData/` is the PostgreSQL access layer. Add new DB access
  there and keep it asynchronous.
- `src/Emby.Server.HttpServer/` owns Kestrel, routing, WebSockets, and static
  serving. Do not create a second HTTP stack.
- `Emby.Server.Implementations/` owns scanning, library, playback, task, and
  other business behavior.
- `web/` is independently developed against the Emby-compatible API. Do not
  make frontend code depend on backend internals.
- `MediaBrowser.WebDashboard/` is legacy UI. Treat it as read-only unless the
  task explicitly targets legacy behavior.
- `emby_pgsql_dump_schema_optimized.sql` is the schema source; startup also
  applies idempotent `PgSchemaInitializer` fixes. Do not create a second schema.

Code is currently reverse-engineered Emby 3.5.3 with a .NET 10/PostgreSQL
modernization. Keep compatibility with existing clients unless the task
explicitly changes the contract.

## C# Style and Safety

- Follow `.editorconfig`: UTF-8, LF, 4-space C# indentation, final newline.
- Match surrounding code style; avoid broad reformatting or unrelated moves.
- Prefer `async`/`await`; do not use `.Result`, `.Wait()`, or blocking stream
  calls in async paths. VSTHRD002/003/100/103/110 are errors in the three
  guarded projects (`src/Emby.Server.PgData`, `Emby.Server.Implementations`,
  `MediaBrowser.Api`).
- Pass `CancellationToken` through async repository/request paths.
- Use parameterized SQL (`NpgsqlParameter`); never interpolate user input.
- Keep time in UTC (`timestamptz`) and preserve the API's Emby-compatible
  PascalCase/ID/error shapes.
- Preserve exception context. Do not swallow errors without a log and a clear
  recovery decision.
- Return 400 for invalid input and 404 for missing resources where the existing
  API conventions permit; do not add new 500s for expected conditions.
- Do not add an ORM or introduce SQLite as a fallback store.

## Frontend Rules

The detailed contract is `web/src/styles/serata/README.md`; it is mandatory for
UI work.

- Align with current Emby Web behavior and preserve the existing Serata
  compatibility contract unless the task explicitly changes it.
- Use only `--s-*` design tokens; no raw colors. New class names use the `s-`
  prefix and scoped styles. Do not invent new `emby-*` class names.
- Use `px`, not `rem`; `:root` is `90.15%`.
- Use `SSheet.vue` for overlays/dialogs. Do not hand-roll `v-if` backdrops.
- Use animation classes/tokens from `motion.css`; no raw `@keyframes`, raw
  durations, or raw cubic-bezier values in components.
- Check `useReducedMotionSafe()` before any JS animation and fully disable
  movement when reduced motion is requested.
- Let `useTheme()` own theme classes; never replace
  `document.documentElement.className`.
- Use `t()` in templates or `computed` values so text reacts to locale changes.
- Every setting must have a read-back path; do not add dead "coming soon" toggles.
- Keep the API prefix and `VITE_*` base-path behavior consistent with
  `web/README.md`.
- Do not edit frozen assets in `web/src/assets/emby495/**` or
  `web/src/assets/emby495-overrides.css` except for an explicit
  removal/migration task.

## API and Data Contracts

- API request/response shapes are anchored to `openapi.json`, current DTOs, and
  real-client observations.
- Do not "clean up" deprecated fields that clients may use.
- Preserve Emby-compatible authentication: `Authorization: MediaBrowser ...`,
  query `api_key`, and signature-compatible playback URLs.
- Preserve the existing DirectPlay > DirectStream > Transcode resolution order.
  Do not make transcoding the default path.
- Scan metadata is canonical; NFO is compatible input, not the database's
  source of truth. A scanner must not delete user data; preserve the existing
  Unbound/Rebind lifecycle.
- Deleted analysis and drafts are not implementation authorization. Check
  current code and tests before coding.

## Tests and Verification

- Add or update tests for behavior changes, especially PG query paths, API
  contracts, and frontend state transitions.
- Validate real browser UI changes; static reasoning and mock-only tests are
  insufficient for layout, focus, overlays, and motion.
- When using CDP probes, keep each run on its own port/profile, use the real UI
  controls for state changes, and never kill other users' browsers/processes.
- Use `localhost:5173` for Vite and do not write UTF-16 PowerShell output.
- Fixture generation and `setup-*.ps1` scripts must remain idempotent.
- Frontend checks should ordinarily include `pnpm type-check` and
  `pnpm build-only`; backend changes should include a Release build and focused
  tests. If a check cannot run because PostgreSQL/ffmpeg/browser is unavailable,
  say so explicitly.

## Commit and Review Guidance

- Keep commits focused; one behavior change per commit where practical.
- Do not revert user changes or unrelated dirty-worktree edits.
- In reviews, lead with bugs, regressions, security/data risks, and missing
  tests; include file/line references. Keep summaries secondary.
- Report real verification evidence; never describe a plan or unrun probe as
  a completed test.
