# Blueberry Mart — Back-Office Portal

A standalone **React + Vite + TypeScript** web app for **admin, manager, and staff** —
the back office for running the store. It is deliberately **separate from the mobile app**
(customers never see it) and talks to the **same .NET API**; the real security boundary is
the API's `[Authorize(Roles = …)]` on the management endpoints, plus per-request ban
enforcement. This portal is just the UI.

> Full feature + endpoint reference: **`Markdown files/Main/BACK_OFFICE_PORTAL.md`**.

## What it does

- **Login** — shared `/api/auth/login`; only `admin`/`manager`/`staff` accounts are let in.
- **Dashboard** — branch-scoped counts (low stock, orders awaiting payment, in fulfillment).
- **Items** — create/edit items, adjust stock (with reason), deactivate/restore
  (manager/admin), and a per-item **stock history** (audit log).
- **Orders** — record cash/card payment, advance fulfillment status, cancel (manager/admin).
- **Reports** (manager/admin) — revenue, order counts, top items over a date range.
- **Admin** (admin only) — users (ban/unban, assign role + branch), reviews, settings,
  maintenance mode.

**Roles are branch-scoped:** staff/manager act only on their assigned branch (enforced via
the JWT `branch` claim, server-side); admins act on any branch. An admin assigns a user's
role and branch from the Users page.

## Run locally

```bash
cp .env.example .env      # VITE_API_URL → your API (default http://localhost:5027)
npm install
npm run dev               # http://localhost:5173  (allowed by the API's CORS)
npm run lint              # ESLint (flat config)
```

You need a back-office account. The API auto-creates an **admin** on startup from
`Admin:Email` / `Admin:Password` (`appsettings.Development.json` locally, Secret Manager in
prod); from there, promote others to staff/manager (with a branch) via the Users page.

## Build & deploy (Firebase Hosting)

Deploys as a static site to **Firebase Hosting** (same GCP project as the API). Config:
`firebase.json` (SPA rewrite + asset caching), `.firebaserc`, and `.env.production` (prod
API URL baked in at build time).

**Deploy is automated:** pushing to `main` with changes under `BlueberryMartPortal/**`
runs the **Portal CI** workflow, which lints, builds, and deploys to Firebase via keyless
Workload Identity Federation. URL: **https://blueberrymart-admin.web.app**.

For a manual one-off:
```bash
npm run deploy            # = npm run build && firebase deploy --only hosting
```

See `Markdown files/CICD_pipeline.md` and `Markdown files/GCP_SERVICES.md` for the pipeline and
cloud setup (CORS origins, admin bootstrap, secrets).
