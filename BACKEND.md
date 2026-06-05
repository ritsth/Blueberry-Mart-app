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

### PostgreSQL enums
- `user_role`: `customer`, `shareholder`
- `order_type`: `pickup`, `delivery`
- `order_status`: `pending`, `confirmed`, `processing`, `ready`, `completed`, `cancelled`
- `payment_status`: `initiated`, `completed`, `failed`

All tables use `uuid` PKs (`gen_random_uuid()`); timestamps are `TIMESTAMPTZ` (UTC).

> **Order lifecycle note:** today the code only sets `pending` (on placement) →
> `confirmed` (on successful payment). Nothing yet advances an order to
> `processing`/`ready`/`completed` — there is no fulfilment/status-management
> endpoint. (`delivered` is **not** a status; the terminal state is `completed`,
> used for both pickup and delivery.)

## Endpoints
| Method & path | Auth | Purpose |
|---|---|---|
| `POST /api/auth/login` | public | Email/password → JWT |
| `POST /api/auth/register` | public | Create a customer account → JWT |
| `POST /api/orders/{id}/receive` | Customer/Shareholder | Mark a confirmed order received (→ completed) |
| `GET /api/branches` | any | List branches |
| `GET /api/inventory/customer?branchId=` | Customer/Shareholder | In-stock, non-bulk items |
| `GET /api/inventory/bulk?branchId=` | Customer/Shareholder | Bulk catalog (members only) |
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
