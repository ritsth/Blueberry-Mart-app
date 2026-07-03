# Contributing to Blueberry Mart

Thanks for helping out. This doc covers the workflow, conventions, and gates for this repo.
For architecture, tech stack, and local setup, start with the [README](README.md).

---

## Getting set up

Follow [Local Development](README.md#local-development) in the README to get the API, database,
and mobile app running. There's also a `CLAUDE.md` in the repo root and in most subfolders
(`BlueberryMart.Api/`, `BlueberryMartApp/`, `BlueberryMartPortal/`, `Tests/`, `Database/`) with
project-specific conventions and non-obvious gotchas — worth a skim before you dig into that area.

---

## Branch & PR workflow

`main` is protected — you can't push to it directly, force-push, or delete it. All changes go
through a pull request:

```bash
git checkout -b your-branch-name
# make your changes, commit
git push -u origin your-branch-name
gh pr create   # or open the PR in the GitHub UI
```

Before a PR can merge:
- **1 approving review** is required.
- **3 CI checks must pass:** `Run Tests` (backend), `TypeScript Type Check` (mobile app),
  `Type Check & Build` (admin portal). Only the checks relevant to what you touched will actually
  run meaningful work, but all three must go green.
- Pushing new commits after approval dismisses the existing review — you'll need a fresh one.

### Branch naming
No strict convention enforced, but prefix with the type of change for clarity, e.g.
`fix/order-total-rounding`, `feat/pagination-notifications`, `docs/update-readme`.

### Commit messages
This repo follows a loose [Conventional Commits](https://www.conventionalcommits.org/) style —
look at `git log` for examples. Common prefixes:

| Prefix | Use for |
|---|---|
| `feat:` | new functionality |
| `fix:` | bug fixes |
| `security:` | security-specific hardening |
| `docs:` | documentation only |
| `ci:` | GitHub Actions / workflow changes |
| `refactor:` | code change with no behavior change |

Keep the summary line short; put the *why* in the body if it's not obvious from the diff.

---

## Before opening a PR

Run the checks locally so you're not waiting on CI to find something you could've caught in
seconds:

```bash
# Backend
dotnet build BlueberryMart.Api
dotnet format BlueberryMart.Api --verify-no-changes   # CI hard-gates on this
dotnet test Tests/BlueberryMart.Api.Tests

# Mobile app
cd BlueberryMartApp && npx tsc --noEmit && npm run lint

# Admin portal
cd BlueberryMartPortal && npm run build && npm run lint
```

If you changed an entity in `Models/Entities/`, add a migration:
```bash
dotnet dotnet-ef migrations add <Name> --project BlueberryMart.Api --output-dir Migrations
```
Migrations apply automatically on startup — no manual `database update` step needed.

---

## Code conventions

- **Backend** — layered architecture (`Controllers` → `Services` → `Repositories`); see the root
  `CLAUDE.md` for the full breakdown and DB conventions (UUID PKs, `TIMESTAMPTZ`, `ON DELETE
  RESTRICT`).
- **Don't add abstractions or config knobs for hypothetical future needs** — match the scope of
  the task.
- **Secrets never go in code, YAML, or docs.** Local secrets go in the gitignored
  `appsettings.Development.json` / `.env.local`; production secrets live in Google Secret Manager.
  If you're documenting a secret's *name* or *purpose*, that's fine — never its value.
- **No demo/seed accounts with known passwords in Production.** `DbInitializer` seeds demo login
  accounts for Development/Testing only — see `RotateLegacyDemoAccountPasswords` in
  `BlueberryMart.Api/Data/DbInitializer.cs` for why this matters.

---

## Reporting a security issue

If you find a vulnerability (exposed secret, auth bypass, IDOR, etc.), please don't open a public
issue. Email **akitirsth@gmail.com** directly instead. See `Markdown files/SECURITY_POSTURE.md`
for the current security posture and known-acceptable tradeoffs.

---

## Questions

Open an issue, or check the docs in `Markdown files/` — there's a write-up for most subsystems
(Kafka pipeline, BigQuery analytics, eSewa payments, Android shipping, etc.).
