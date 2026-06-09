# CI/CD

How Blueberry Mart builds, checks, and deploys. All automation lives in
`.github/workflows/` and runs on GitHub Actions. The repo is **public**, so
GitHub Advanced Security (CodeQL) and Actions minutes are free.

## At a glance

| Workflow | File | Triggers on changes to | Checks / jobs |
|---|---|---|---|
| **Test & Deploy to Cloud Run** | `deploy.yml` | `BlueberryMart.Api/**`, `Tests/**`, self | tests · format gate · NuGet vuln scan · cached Docker build → Cloud Run |
| **Mobile App CI** | `frontend-typecheck.yml` | `BlueberryMartApp/**`, self | `ESLint` · `TypeScript Type Check` (+ npm audit, non-blocking) |
| **Admin Portal CI** | `admin-build.yml` | `BlueberryMartAdmin/**`, self | `ESLint` · `Type Check & Build` (+ npm audit, non-blocking) |
| **CodeQL** | `codeql.yml` | every push/PR to `main` + weekly | SAST: `csharp` + `javascript-typescript` |

Path filters mean a backend-only change doesn't run the frontend workflows and
vice versa. CodeQL runs on everything.

---

## Backend — `deploy.yml`

Two jobs; **deploy only runs if tests pass** (`needs: test`).

**Job 1 — Run Tests** (`ubuntu-latest`, with a `postgres:16` service container):
1. Checkout + set up .NET 8.
2. `dotnet restore`.
3. **Scan for vulnerable NuGet packages** — `dotnet list package --vulnerable
   --include-transitive`. The command always exits 0, so the step greps its
   output and **fails the build** if any advisory is found. (Blocking.)
4. **Format gate** — `dotnet format BlueberryMart.Api --verify-no-changes`.
   Fails if code isn't formatted to the conventions in `.editorconfig`.
5. **Integration tests** — `dotnet test Tests/BlueberryMart.Api.Tests` against
   the real Postgres service container.

**Job 2 — Build & Deploy** (`needs: test`):
1. Authenticate to Google Cloud via **Workload Identity Federation** (no
   long-lived keys; `WIF_PROVIDER` / `WIF_SERVICE_ACCOUNT` repo secrets).
2. **Build + push the image with a layer cache** — `docker/build-push-action`
   with `cache-from/to: type=gha`. Unchanged layers (base image, NuGet restore)
   are reused across deploys instead of rebuilt.
3. `gcloud run deploy` to the `blueberrymart-api` Cloud Run service, setting the
   production env vars (GCS bucket, eSewa URLs, BigQuery project, etc.).

Because the app calls `context.Database.Migrate()` on startup, deploying new
code **applies pending EF Core migrations automatically** — no manual step.

## Mobile app — `frontend-typecheck.yml` (Expo / React Native)

Two **parallel jobs**, each its own status check:
- **ESLint** — `npm run lint` (`eslint-config-expo`, flat config). Blocking on
  errors; the existing `exhaustive-deps` findings are warnings (eslint exits 0
  on warnings), so this gates *new* errors only.
- **TypeScript Type Check** — `tsc --noEmit`, then a non-blocking `npm audit
  --audit-level=high` (Expo/RN pull in transitive advisories we often can't fix
  directly).

## Admin portal — `admin-build.yml` (React / Vite)

Two **parallel jobs**:
- **ESLint** — `npm run lint` (`typescript-eslint` + `react-hooks` +
  `react-refresh`). Blocking.
- **Type Check & Build** — `npm run build` (`tsc -b && vite build`), then a
  non-blocking `npm audit`. This catches type errors and build breaks before
  the manual `npm run deploy` to Firebase Hosting.

## Security — `codeql.yml`

GitHub's CodeQL SAST over a language matrix (`csharp`,
`javascript-typescript`), using **`build-mode: none`** (source-level analysis,
no compile step needed). Runs on push/PR to `main` and weekly (`cron`). Findings
appear under **Security → Code scanning** — alerts there are independent of
whether the workflow succeeds.

---

## Conventions

- **`.editorconfig`** (repo root) documents the formatting rules the backend
  format gate enforces. It is behaviour-neutral — only rules the codebase
  already satisfies, kept intentionally light so the auto-generated EF
  migrations don't fail the gate.
- **Blocking vs. non-blocking:** lint, format, tests, and the NuGet vuln scan
  are blocking. `npm audit` is non-blocking (`continue-on-error: true`) until
  the transitive-advisory backlog is clean, then it can be promoted.
- **Separate jobs over steps:** lint is split from build/type-check into its own
  parallel job so each shows as a distinct status line and a lint failure
  doesn't hide the build result.

## Changelog — 2026-06-08/09

Work done to elevate the pipeline from build-and-deploy to a multi-layered
gatekeeper:

| Commit | Change |
|---|---|
| `29e6352` | Added Admin Portal CI; cached the backend Docker build (`build-push-action` + gha cache) |
| `7f399d8` | Added `.editorconfig` documenting the format gate |
| `38c0d44` | Added dependency vuln scanning — `dotnet list --vulnerable` (blocking) + `npm audit` (non-blocking) |
| `aeee3c3` | Added ESLint to the admin portal |
| `1b11062` | Added ESLint to the mobile app (+ fixed the 4 errors it surfaced) |
| `e2fded5` | Split frontend lint into its own parallel job |
| `1b258d0` | Added CodeQL static analysis |

## Open items / how to extend

- **Admin → Firebase auto-deploy:** currently manual (`npm run deploy`). To
  automate, add a deploy job to `admin-build.yml` gated on the build job, using
  a Firebase service-account stored as a GitHub secret.
- **`exhaustive-deps` warnings (app):** `ReportChart.tsx:149`,
  `ShoppingView.tsx:77`, `ExploreTab.tsx:80` & `:89`. Fix case by case (changing
  dependency arrays can alter runtime behaviour), then the rule can go blocking.
- **Promote `npm audit` to blocking** once each frontend's advisory list is
  clean.
- **Adding a new check:** prefer a new parallel job (clear status line) over a
  step inside an existing job.
