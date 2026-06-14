# Back-Office Portal

`BlueberryMartPortal/` — a single role-aware web portal for **admin, manager, and
staff**, backed by role-gated endpoints on the same .NET API the mobile app uses. It is
the back office (operations + administration); customers only ever use the mobile app.

- **Live:** https://blueberrymart-admin.web.app  (Firebase Hosting site id
  `blueberrymart-admin` — kept on rename, so the URL is unchanged)
- **Stack:** React 18 + Vite 5 + TypeScript, React Router
- Formerly `BlueberryMartAdmin/` (admin-only); renamed 2026-06-09 when manager/staff shipped.

The real security boundary is the **backend** — every back-office endpoint is
`[Authorize(Roles = …)]`. Keeping this code out of the mobile binary is only
defence-in-depth.

## Architecture

```
Mobile app (Expo)  ─┐
                    ├─►  BlueberryMart.Api (Cloud Run)  ──►  Cloud SQL (Postgres)
Portal (SPA)       ─┘        /api/admin/*        [Admin]
   Firebase Hosting          /api/inventory/manage/*   [Staff,Manager,Admin]
                             /api/orders/manage/*       [Staff,Manager,Admin]
                             /api/dashboard/summary     [Staff,Manager,Admin]
                             /api/reports/sales         [Manager,Admin]
                             /api/system/status         [AllowAnonymous]
```

One shared backend; the portal talks to it via CORS (`Cors:PortalOrigins`).

## Roles & branch scoping

| Role | Logs into portal | Scope | Can do |
|------|:---:|---|---|
| `customer` | — | — | mobile only |
| `shareholder` | — | — | mobile analytics |
| `staff` | ✓ | one branch | items, stock, order fulfillment, take payments |
| `manager` | ✓ | one branch | staff + deactivate items, cancel orders, reports |
| `admin` | ✓ | all branches | everything + users/reviews/settings |

- **Branch scoping** — `User.BranchId` (nullable FK → branches; migration `AddUserBranch`)
  is set for staff/manager, null for everyone else. The login JWT carries a **`branch`
  claim**. Every management endpoint checks it: staff/manager get **403** acting outside
  their branch; admins are unrestricted. Admin assigns a user's role + branch on the Users
  page (`POST /api/admin/users/{id}/role` with `{ role, branchId }`).
- The `role` column is plain `text` (the `user_role` enum exists but isn't typed to it), so
  adding roles needed **no enum migration**.
- **Portal access**: login (`auth.ts`) accepts only `admin`/`manager`/`staff`; customers/
  shareholders are rejected. Client routing mirrors the server (`RequireAuth`,
  `RequireAdmin`, `RequireManager`); the nav shows only what the role can use.

## Feature areas

### Dashboard
Branch-scoped at-a-glance counts (low-stock items, orders awaiting payment, orders in
fulfillment) as clickable stat cards. `GET /api/dashboard/summary` (`DashboardController`).
Admin counts span all branches.

### Items & stock (catalogue management)
The `Inventory` row **is** the item (per-branch `ItemName`, `Price`, `StockQuantity`,
`IsBulkOnly`, `IsActive`). Portal **Items** page:
- Searchable/paginated table, low-stock + show-inactive filters.
- **Create / edit** via modals (admins pick a branch; staff/manager scoped to theirs).
- **Adjust stock** modal — add/remove a quantity with a reason, live new-total preview.
- **Deactivate / restore** — soft-delete (`IsActive`); orders reference items so they're
  never hard-deleted. Manager/admin only. Inactive items hidden from customers.
- **History** modal — the stock audit trail (below).

### Stock audit log
Every back-office stock adjustment writes a `StockAdjustment` row (migration
`AddStockAdjustments`): who, delta, resulting quantity, reason, when. Viewed per item via
`GET /api/inventory/manage/{id}/history` (last 50).

### Orders & payments (fulfillment)
Portal **Orders** page: table with status + payment pills, status/order-# filters, and a
detail modal (line items, totals, delivery address) with contextual actions:
- **Record manual payment** (cash/card) for a `pending` order — mirrors the eSewa success
  path: marks paid, moves `pending → confirmed`, credits loyalty points.
- **Advance status** along the linear chain `confirmed → processing → ready → completed`
  (non-linear jumps rejected; unpaid orders can't be advanced).
- **Cancel** (manager/admin) — sets `cancelled` and **restocks** the order's items. A **paid**
  order can only be cancelled while still `pending`/`confirmed` (pre-fulfilment) — that's a refund,
  so it drops out of revenue (cancelled ∩ paid) while staying analyzable via the Explore
  `order_status` dimension.
In-store walk-in sales are rung up on the dedicated **Sell** page (below), not here.

### Sell (in-store point of sale)
Portal **Sell** page (`SellPage`, all back-office roles) — a till for ringing up walk-ins:
- Two-pane layout: searchable branch catalogue (active, in-stock, **retail only — bulk excluded**)
  on the left; a running **ticket** (line items, qty steppers, **running total**) on the right, plus
  a payment method (cash/card/eSewa) and **Complete sale**. Bulk is members-only wholesale, so it
  isn't sold at the walk-in till (the API rejects bulk items too).
- Staff/managers sell at their own branch; admins pick a branch first.
- **Attach customer (optional):** search shoppers by email **or phone**
  (`GET /api/orders/manage/customers?q=`) and attach one to credit loyalty / record it in their
  history; a member shows a discount line. **+ New customer** quick-creates a *guest* from just a
  phone (`POST /api/orders/manage/customers`, idempotent by phone) so first-timers start earning
  loyalty. Left blank, the sale is an anonymous walk-in (null `UserId`, shown as "Walk-in").
- **Complete sale** → `POST /api/orders/manage/in-store-sale`, which creates a paid, `completed`,
  `channel=in_store` order in one shot and deducts stock. Pops a **printable receipt** (branch,
  order #, date, cashier, customer/Walk-in, line items, subtotal/discount/total, payment) with a
  **Print** button (`@media print` shows only the receipt) and **New sale**; the ticket clears and
  the sale appears on the Orders page as `completed` / `in_store`.
- **Dashboard placement:** staff land **directly on the till** as their home (it's their main job),
  via a `<SellPage embedded />` on the dashboard; managers/admins keep the stats Dashboard. The
  sidebar **Sell** link works for everyone.

### Reports
**Reports** page (manager/admin only): revenue (paid orders), paid-order count, average
order value, an orders-by-status breakdown, and top items, over a chosen date range.
`GET /api/reports/sales?from&to&branchId` (`ReportsController`). Managers see their branch;
admins any branch or all.

### Admin pages (admin only)
- **Users** — search/filter; ban/unban (last admin can't be demoted); assign role + branch.
- **Reviews** — list and delete inappropriate ones.
- **Settings** — delivery fee, membership fee, member discount, and maintenance mode.

## Endpoint reference

| Method | Route | Roles | Purpose |
|--------|-------|-------|---------|
| GET | `/api/dashboard/summary` | Staff,Manager,Admin | branch counts |
| GET | `/api/inventory/manage` | Staff,Manager,Admin | list items (search/lowStock/includeInactive) |
| POST | `/api/inventory/manage` | Staff,Manager,Admin | create item |
| PUT | `/api/inventory/manage/{id}` | Staff,Manager,Admin | edit name/price/bulk |
| POST | `/api/inventory/manage/{id}/adjust` | Staff,Manager,Admin | signed stock delta + reason |
| POST | `/api/inventory/manage/{id}/deactivate` · `/activate` | Manager,Admin | soft-delete / restore |
| GET | `/api/inventory/manage/{id}/history` | Staff,Manager,Admin | stock audit log |
| GET | `/api/orders/manage` | Staff,Manager,Admin | list orders (status/order-#) |
| GET | `/api/orders/manage/{id}` | Staff,Manager,Admin | order detail (items + payment) |
| POST | `/api/orders/manage/{id}/status` | Staff,Manager,Admin | advance fulfillment status |
| POST | `/api/orders/manage/{id}/record-payment` | Staff,Manager,Admin | manual/cash payment |
| POST | `/api/orders/manage/in-store-sale` | Staff,Manager,Admin | ring up a walk-in sale (paid + completed, channel `in_store`) |
| POST | `/api/orders/manage/{id}/cancel` | Manager,Admin | cancel + restock |
| GET | `/api/reports/sales` | Manager,Admin | branch sales report |
| GET | `/api/admin/users` | Admin | list/search users |
| POST | `/api/admin/users/{id}/ban` · `/unban` | Admin | moderation |
| POST | `/api/admin/users/{id}/role` | Admin | assign role + branch |
| GET/DELETE | `/api/admin/reviews[/{id}]` | Admin | review moderation |
| GET/PUT | `/api/admin/settings` | Admin | global settings |
| GET | `/api/system/status` | Anonymous | maintenance + public settings (mobile banner) |

All branch-scoped endpoints enforce the JWT `branch` claim for staff/manager.

## Ban enforcement (immediate)
JWTs are stateless but bans must take effect now: a JwtBearer `OnTokenValidated` hook
(`Program.cs`) looks up the user every authenticated request and `ctx.Fail()`s if they're
banned or deleted. One indexed lookup; a banned user's existing token is rejected on the
next call.

## Bootstrapping the admin account
On startup, if no admin exists and `Admin:Email` + `Admin:Password` are set, the API
creates one (or promotes the existing email). Password lives in **Secret Manager**
(`admin-password`) in prod and the gitignored `appsettings.Development.json` locally.

## Local dev & deploy

```bash
# local
cp .env.example .env          # VITE_API_URL=http://localhost:5027
npm install
npm run dev                   # http://localhost:5173  (allowed by the API's CORS)
npm run lint                  # ESLint (flat config)
```

**Deploy is automated.** Push to `main` touching `BlueberryMartPortal/**` runs the
**Portal CI** workflow (`portal-ci.yml`): ESLint · type-check & build · **Deploy to
Firebase Hosting** (keyless Workload Identity Federation, same SA as the backend deploy,
granted `roles/firebasehosting.admin`). `npm run deploy` still works for manual one-offs.
The backend deploys via the **Test & Deploy** workflow; EF migrations apply on boot. See
[CICD_pipeline.md](../CICD_pipeline.md) and [GCP_SERVICES.md](../GCP_SERVICES.md).

Production Cloud Run wiring (CORS + admin bootstrap):
```
Cors__PortalOrigins__0=https://blueberrymart-admin.web.app
Cors__PortalOrigins__1=https://blueberrymart-admin.firebaseapp.com
ADMIN__EMAIL=admin@firebase.com
ADMIN__PASSWORD → Secret Manager secret admin-password:latest
```

## Settings store
A single-row `store_settings` table (migration `AddStoreSettings`), read via
`ISettingsService` (cached in `IMemoryCache`, invalidated on update). Replaced hardcoded
constants for delivery fee, membership fee, and member discount. **Maintenance mode**: when
on, `POST /api/orders` returns **503**; the mobile app shows an app-wide banner by polling
`/api/system/status`. Seeded with the former defaults (100 / 199 / 0.05).

## Tests
Integration tests in `Tests/BlueberryMart.Api.Tests/` run in CI against a real Postgres:
- `AdminControllerTests`, `AdminSettingsTests` — auth/403/401, ban lifecycle, role + last-admin
  guards, settings round-trip, maintenance blocks ordering.
- `ManageInventoryControllerTests` — own-branch create/adjust, cross-branch 403, manager-only
  deactivate, adjust writes a history row.
- `ManageOrdersControllerTests` — record payment, advance status, non-linear rejection,
  cross-branch denial, manager-only cancel.
- `DashboardControllerTests`, `ReportsControllerTests` — scope + role gating; reports reflect a
  paid order.
- `MembershipControllerTests` — shareholders/admins are members automatically.
