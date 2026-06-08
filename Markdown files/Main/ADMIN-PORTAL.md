# Admin Portal

A separate web portal for administrators, backed by role-gated endpoints on the same
.NET API the mobile app uses. Admin code is deliberately kept **out of the mobile
binary** (defence-in-depth) — but the real security boundary is the **backend**:
every admin endpoint is `[Authorize(Roles = "Admin")]`.

## Architecture

```
Mobile app (Expo)  ─┐
                    ├─►  BlueberryMart.Api (Cloud Run)  ──►  Cloud SQL (Postgres)
Admin portal (SPA) ─┘        /api/admin/*  [Authorize(Roles="Admin")]
   Firebase Hosting          /api/system/status  [AllowAnonymous]
```

- **API** — one shared backend. Admin routes live under `/api/admin/*`.
- **Portal** — `BlueberryMartAdmin/`, a React + Vite + TypeScript static SPA on
  Firebase Hosting. Talks to the API via CORS (`Cors:PortalOrigins`).
- **Mobile** — only consumes the public `/api/system/status` (maintenance banner).

## Roles

| Role | Where | Notes |
|------|-------|-------|
| `customer` | mobile | default on sign-up |
| `shareholder` | mobile | read-only analytics |
| `admin` | portal | this feature |
| `staff` / `manager` | — | **Wave 2** — assignable now but inert until branch-scoped capabilities ship |

The `role` column is plain `text` (the `user_role` enum type exists but the column
isn't typed to it), so adding `admin` needed **no enum migration**.

## What an admin can do (Wave 1)

- **Users** — search/filter; **ban / unban**; **assign role** (the last remaining
  admin can't be demoted).
- **Reviews** — list and **delete** inappropriate ones.
- **Settings** — edit formerly-hardcoded values (**delivery fee, membership fee,
  member discount**) and toggle **maintenance mode**.

### Endpoints (`AdminController`, all `[Authorize(Roles="Admin")]`)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/admin/users` | list/search (`search`, `role`, `banned`, `page`, `pageSize`) |
| POST | `/api/admin/users/{id}/ban` | body `{ reason }`; blocks self & other admins |
| POST | `/api/admin/users/{id}/unban` | |
| POST | `/api/admin/users/{id}/role` | body `{ role }`; blocks demoting last admin |
| GET | `/api/admin/reviews` | list recent reviews |
| DELETE | `/api/admin/reviews/{id}` | remove a review |
| GET / PUT | `/api/admin/settings` | read / patch global settings |

Public: `GET /api/system/status` → `{ maintenanceMode, maintenanceMessage,
deliveryFee, membershipMonthlyFee, memberDiscountRate }`.

## Ban enforcement (immediate)

JWTs are stateless, but bans must take effect now — not at token expiry. A JwtBearer
`OnTokenValidated` hook (`Program.cs`) looks up the user on every authenticated request
and calls `ctx.Fail()` if they're banned (or no longer exist). One indexed lookup per
request; a banned user's existing token is rejected on the **next** call.

## Settings store

A single-row `store_settings` table (migration `AddStoreSettings`), read through
`ISettingsService` and cached in `IMemoryCache` (invalidated on update). It replaced the
old constants:

- `OrdersController.DeliveryFee` (const) → `settings.DeliveryFee`
- `MembershipController.MonthlyFee` / `MemberDiscountRate` (consts) → settings
- **Maintenance mode**: when on, `POST /api/orders` returns **503** with the configured
  message; the cart checkout surfaces it, and the mobile app shows an app-wide banner
  (`MaintenanceBanner`) by polling `/api/system/status` (60s + on foreground).

Seeded on startup with the former defaults (delivery 100 / membership 199 / discount
0.05) via `DbInitializer.EnsureSettings`.

## Bootstrapping the admin account

On startup, if no admin exists and `Admin:Email` + `Admin:Password` are set, the API
creates one (or promotes the existing email). Password lives in **Secret Manager** in
prod and the gitignored `appsettings.Development.json` locally — never in git.

## The portal (`BlueberryMartAdmin/`)

Stack: React 18 + Vite 5 + TypeScript, React Router. Pages: Login (admin-only),
Users, Reviews, Settings.

```bash
# local
cp .env.example .env          # VITE_API_URL=http://localhost:5027
npm install
npm run dev                   # http://localhost:5173  (allowed by the API's CORS)

# deploy (Firebase Hosting)
npm run deploy                # build + firebase deploy --only hosting
```

Production URL: `https://blueberrymart-admin.web.app`.

## Production wiring (Cloud Run env)

```bash
# CORS — both Firebase URLs
Cors__PortalOrigins__0=https://blueberrymart-admin.web.app
Cors__PortalOrigins__1=https://blueberrymart-admin.firebaseapp.com
# Admin bootstrap
ADMIN__EMAIL=admin@firebase.com
ADMIN__PASSWORD  → Secret Manager secret `admin-password:latest`
```

Set via `gcloud run services update blueberrymart-api --region us-central1
--update-env-vars … --update-secrets …`. The backend deploys via GitHub Actions on push
to `main` (paths `BlueberryMart.Api/**`, `Tests/**`); migrations apply automatically on
boot.

## Tests

`Tests/BlueberryMart.Api.Tests/AdminControllerTests.cs` and `AdminSettingsTests.cs`
cover: non-admin 403 / no-token 401, mid-session ban → 401 → unban → 200, ban-self and
ban-admin guards, settings round-trip + validation, maintenance blocks ordering, role
assignment, last-admin demote guard.

## Wave 2 (deferred)

Branch-scoped **staff** / **manager** roles + the operations screens they need
(inventory, order fulfilment, refunds). Requires a branch association on users and
scope-enforcing authorization. Build when there are actual store staff to onboard.
