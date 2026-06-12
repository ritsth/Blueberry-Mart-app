# Blueberry Mart — Backend Overview

The backend is a **.NET 8 Web API** (`BlueberryMart.Api/`) backed by **PostgreSQL**
(Cloud SQL in production) via **Entity Framework Core 8 + Npgsql**. It powers the
Blueberry Mart grocery app: catalog, ordering, eSewa payments, reviews, loyalty,
membership, and shareholder analytics.

## Stack
- .NET 8 Web API, C#
- EF Core 8 + Npgsql; schema is managed by **EF Core migrations**
  (`Migrations/`), applied automatically on startup (`DbInitializer.Initialize`
  → `context.Database.Migrate()`).
- JWT bearer auth; Swagger in Development.
- Deployed to **Google Cloud Run** via GitHub Actions.

## Architecture (layered, inside `BlueberryMart.Api/`)
```
Controllers/   HTTP entry points (thin)
Services/      business logic (e.g. EsewaPaymentService, review image storage)
  Interfaces/
Models/
  Entities/    DB rows
  Requests/    request bodies
Configuration/ strongly-typed settings (EsewaOptions)
Data/          DbContext, DbInitializer, design-time factory
Migrations/    EF Core migrations (source of truth for schema)
wwwroot/       static files (eSewa result pages)
```

## Data model (`Models/Entities/`)
- **User** — email, password hash, `role`, loyalty points, membership window
  (`MemberSince`/`MemberUntil`/`MembershipCancelled`, computed `IsMember`).
- **Branch** — store location.
- **Inventory** — item per branch (name, price, stock, `IsBulkOnly`).
- **Order** — `OrderNumber` (sequential from 1001), `OrderType`, `Status`,
  `TotalAmount`/`DiscountAmount`/`DeliveryFee`, delivery snapshot.
- **OrderItem** — line items (item, qty, unit price).
- **Payment** — one per order; eSewa `TransactionUuid`, amount, `Status`,
  `ProviderRef`.
- **Review** — per order+item; rating, comment, optional image path.
- **Address** — customer delivery addresses.
- **SavedReport** — a shareholder's saved "Explore" chart: a `name` + the query
  config as `jsonb` (config only, never data). Scoped per shareholder.

### PostgreSQL enums
- `user_role`: `customer`, `shareholder`
- `order_type`: `pickup`, `delivery`
- `order_status`: `pending`, `confirmed`, `processing`, `ready`, `completed`, `cancelled`
- `payment_status`: `initiated`, `completed`, `failed`

All tables use `uuid` PKs (`gen_random_uuid()`); timestamps are `TIMESTAMPTZ` (UTC).

> **Order lifecycle:** `pending` (placement) → `confirmed` (eSewa success or manual
> record-payment) → `processing` → `ready` → `completed`. The linear fulfilment chain is
> advanced by staff via `POST /api/orders/manage/{id}/status`; the customer's `receive`
> endpoint also moves `confirmed → completed`. `cancelled` is terminal — set by a
> manager cancel or by unpaid-order expiry; a **paid** order can only be cancelled while
> still `pending`/`confirmed` (pre-fulfilment), which is treated as a refund. (`delivered`
> is **not** a status; `completed` is the terminal state for both pickup and delivery.)
> Every post-placement status change emits an `order_status_changed` sales event, which
> drives the Explore `order_status` dimension; **dashboard/Explore revenue counts only
> collected money = a completed payment AND `order_status != cancelled`.**

## Endpoints
| Method & path | Auth | Purpose |
|---|---|---|
| `POST /api/auth/login` | public | Email/password → JWT |
| `POST /api/auth/register` | public | Create a customer account → JWT |
| `POST /api/orders/{id}/receive` | Customer/Shareholder | Mark a confirmed order received (→ completed) |
| `GET /api/branches` | any | List branches |
| `GET /api/inventory/customer?branchId=` | Customer/Shareholder | In-stock, non-bulk items |
| `GET /api/inventory/bulk?branchId=` | Customer/Shareholder | Bulk catalog (members only) |
| `GET /api/inventory/top?branchId=&bulk=` | Customer/Shareholder | Branch best sellers (in-stock items ranked by units sold; bulk=true needs membership) |
| `GET /api/inventory/shareholder` | Shareholder | Full inventory |
| `GET /api/inventory/search?q=` | Customer/Shareholder | Search across branches |
| `POST /api/inventory/{id}/restock` | Shareholder | Add stock; emits a stock-changed event |
| `POST /api/inventory/{id}/notify-me` | Customer/Shareholder | Subscribe to back-in-stock for an item |
| `GET /api/notifications` · `POST /api/notifications/read` | any | List / mark-read in-app notifications |
| `POST /api/orders` | Customer/Shareholder | Place an order (creates `pending`) |
| `GET /api/orders/{id}` | Customer/Shareholder | Order + payment status (owner only) |
| `POST /api/payments/esewa/initiate` | Customer/Shareholder | Start eSewa payment |
| `GET /api/payments/esewa/success` | public | eSewa success callback |
| `GET /api/payments/esewa/failure` | public | eSewa failure callback |
| `POST /api/reviews` | Customer/Shareholder | Submit a review (multipart) |
| `GET /api/profile` | any | Profile + orders (with items) + reviews |
| `GET /api/addresses` · `POST` · `PUT /{id}/default` · `DELETE /{id}` | any | Manage addresses |
| `GET /api/membership/status` · `POST /activate` · `POST /cancel` | any | Blueberry Plus membership |
| `GET /api/shareholders/analytics` | Shareholder | Sales analytics + charts |
| `GET /api/shareholders/inventory-analytics` | Shareholder | Stock-movement analytics from BigQuery (Kafka pipeline) |
| `GET /api/analytics/catalog` | Shareholder | Introspected field catalog (dimensions + measures) for the Explore builder |
| `POST /api/analytics/query` | Shareholder | Run a validated, parameterized aggregation over the BigQuery `sales_fact` warehouse |
| `GET/POST /api/analytics/reports` · `GET/PUT/DELETE /{id}` | Shareholder | CRUD for saved Explore chart configs (per shareholder) |

## Key business rules
- **Membership (Blueberry Plus):** 5% member discount on goods, free delivery
  (non-members pay a flat Rs 100 delivery fee); members unlock the bulk catalog.
- **Loyalty points:** 1 point per whole unit of goods value, credited **on payment
  completion** (not at placement). Reviews earn 10 points (text) / 20 (with photo).
- **Payments (eSewa ePay-v2):** see `EsewaPaymentPlan.md` for the full flow. Sandbox
  by default; order becomes `confirmed` only after a verified, status-checked
  payment.
- **Reviews:** one per order+item (duplicates blocked); item must belong to the
  order's branch.

## Auth
JWT bearer. Roles: `Customer`, `Shareholder`. Token carries the user id
(`NameIdentifier`) and role; controllers gate with `[Authorize(Roles=…)]`.

## Config & secrets
- Local: `appsettings.Development.json` (dev DB + dev JWT). eSewa sandbox defaults
  live in `appsettings.json`.
- Production (Cloud Run): `ConnectionStrings__DefaultConnection` and `Jwt__Secret`
  come from **Secret Manager**; `ESEWA__*` are env vars set in the deploy step.

## Database & migrations
Change an entity in `Models/Entities/`, then:
```bash
dotnet dotnet-ef migrations add <Name> --project BlueberryMart.Api --output-dir Migrations
```
Migrations apply automatically on the next startup/deploy.

## Build / run / test
```bash
dotnet run   --project BlueberryMart.Api
dotnet build BlueberryMart.Api
dotnet test  Tests/BlueberryMart.Api.Tests
```

## Deploy
GitHub Actions (`.github/workflows/deploy.yml`) on push to `main` touching
`BlueberryMart.Api/**`, `Tests/**`, or the workflow: runs `dotnet format
--verify-no-changes` (hard gate) + integration tests, then builds/pushes the image
and `gcloud run deploy`s to **Cloud Run**
(`https://blueberrymart-api-278293545480.us-central1.run.app`), connected to Cloud
SQL instance `blueberrymart-db`.
