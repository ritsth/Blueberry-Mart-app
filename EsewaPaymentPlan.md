# Add eSewa payment to Blueberry Mart

## Context

Orders today are created straight to `Status = "pending"` in
`OrdersController.PlaceOrder` with **no payment step at all** — `Order` carries
`TotalAmount`/`DiscountAmount`/`DeliveryFee` but nothing records *how* or *whether*
an order was paid. There is no `Payment` entity, no provider integration, and order
status never transitions after creation.

We want customers to pay for an order through **eSewa** (the only payment method)
before it is confirmed. The API serves a **mobile app**, so eSewa's redirect URLs
are backend endpoints that verify the result and then 302-redirect to app **deep
links**. We target eSewa's **sandbox** by default (test endpoints, `EPAYTEST`
merchant code), with real credentials supplied via config/env in production.

Integration uses eSewa's **ePay v2** flow: a signed form is opened in the app's
webview → user pays → eSewa redirects to our success/failure URL with a base64
`data` payload → we verify the HMAC signature and confirm via the status-check API.

## Approach

### 1. Data model — `Payment` entity

New `Models/Entities/Payment.cs`:
- `Id` (uuid, `gen_random_uuid()`)
- `OrderId` (uuid, FK → orders, **unique** — one payment per order)
- `TransactionUuid` (string, **unique**) — our reference sent to eSewa as
  `transaction_uuid` (a fresh `Guid` string; eSewa allows alphanumeric + `-`)
- `Amount` (`numeric(12,2)`) — equals the order `total_amount`
- `Status` (`payment_status` enum: `initiated`, `completed`, `failed`)
- `ProviderRef` (string?, nullable) — eSewa `transaction_code` returned on success
- `CreatedAt`, `UpdatedAt` (`TIMESTAMPTZ`, `NOW()` defaults)

Wire into `Data/BlueberryMartDbContext.cs` following existing conventions
(snake_case columns, `HasDefaultValueSql`, FK `OnDelete(Restrict)`, index on
`OrderId`/`TransactionUuid`):
- Add `public DbSet<Payment> Payments => Set<Payment>();`
- Add `modelBuilder.HasPostgresEnum("payment_status", ["initiated","completed","failed"])`
  alongside the existing enums (line ~20).
- Add a `modelBuilder.Entity<Payment>(...)` block mirroring the `Order` config.

Generate the migration:
`dotnet dotnet-ef migrations add AddPayments --project BlueberryMart.Api --output-dir Migrations`
(applies automatically on startup via `DbInitializer`).

### 2. Config — strongly-typed `EsewaOptions`

New `Configuration/EsewaOptions.cs` (the `Configuration/` folder is referenced in
CLAUDE.md but doesn't exist yet — create it). Bound from an `"Esewa"` section:
- `FormUrl` — sandbox: `https://rc-epay.esewa.com.np/api/epay/main/v2/form`
- `StatusUrl` — sandbox: `https://rc.esewa.com.np/api/epay/transaction/status/`
- `MerchantCode` — sandbox: `EPAYTEST`
- `SecretKey` — sandbox: `8gBm/:&EnhH.1/q`
- `ApiBaseUrl` — this API's public base, used to build the success/failure URLs
  eSewa calls back (e.g. `https://localhost:5001`)
- `SuccessDeepLink` / `FailureDeepLink` — app deep links to redirect to after
  verification (e.g. `blueberrymart://payment/success`)

Add the `"Esewa"` section to `appsettings.json` (non-secret sandbox defaults) and
note in the section that `Esewa:SecretKey`/`Esewa:MerchantCode` are overridden by
`ESEWA__SECRETKEY` etc. in production. Bind via
`builder.Services.Configure<EsewaOptions>(builder.Configuration.GetSection("Esewa"))`
in `Program.cs`.

### 3. Service — `EsewaPaymentService`

New `Services/Interfaces/IEsewaPaymentService.cs` + `Services/EsewaPaymentService.cs`:
- `EsewaFormPayload BuildInitiationPayload(Payment payment, decimal totalAmount)` —
  builds the field set (`amount`, `tax_amount=0`, `total_amount`,
  `transaction_uuid`, `product_code`, `product_service_charge=0`,
  `product_delivery_charge=0`, `success_url`, `failure_url`, `signed_field_names`,
  `signature`) and returns `{ FormUrl, Fields }`.
- `string Sign(string message)` — `Base64(HMACSHA256(secretKey, message))`.
  Initiation message: `total_amount={t},transaction_uuid={u},product_code={c}`.
- `EsewaCallbackResult VerifyAndDecode(string base64Data)` — decode JSON, recompute
  signature over the `signed_field_names` fields and compare (constant-time).
- `Task<bool> ConfirmViaStatusApiAsync(string txnUuid, decimal total, CancellationToken)` —
  GET `StatusUrl?product_code=..&total_amount=..&transaction_uuid=..`, require
  `status == "COMPLETE"`. Uses an injected `HttpClient` (register with
  `builder.Services.AddHttpClient<IEsewaPaymentService, EsewaPaymentService>()` —
  mirrors the existing GCS-service-via-config pattern in `Program.cs`).

Register the service in `Program.cs`.

### 4. Endpoints — `Controllers/PaymentsController.cs`

**`POST /api/payments/esewa/initiate`** — `[Authorize(Roles="Customer,Shareholder")]`
- Body: `{ orderId }`. Load order; verify it belongs to the caller
  (`ClaimTypes.NameIdentifier`, as in `OrdersController`), is `pending`, and has no
  `completed` payment.
- Reuse an existing `initiated` payment for the order or create a new one
  (status `initiated`, fresh `TransactionUuid`, `Amount = order.TotalAmount`).
- Return `{ formUrl, fields }` for the app to POST into a webview.

**`GET /api/payments/esewa/success?data=<base64>`** — **`[AllowAnonymous]`**
(browser redirect carries no JWT; trust comes from signature + status-check):
- `VerifyAndDecode` the payload; if signature invalid → redirect to `FailureDeepLink`.
- Match `transaction_uuid` to a `Payment`; `ConfirmViaStatusApiAsync` for defence in
  depth. In a DB transaction: set payment `completed` + `ProviderRef`, transition the
  order `pending → confirmed`, set `UpdatedAt`. Idempotent (no-op if already
  `completed`). Then 302 → `SuccessDeepLink?orderId=..`.

**`GET /api/payments/esewa/failure?data=<base64>`** — `[AllowAnonymous]`:
- Mark the matching `initiated` payment `failed` (if found); 302 → `FailureDeepLink`.

### 5. Move loyalty crediting to payment completion

Currently `OrdersController.PlaceOrder` (lines 128–133) credits loyalty points at
**order placement**, before any payment. Since orders must now be paid before
confirmation, move the `user.LoyaltyPoints += (int)Math.Floor(goodsTotal)` credit
into the success callback (compute from the confirmed order's
`TotalAmount - DeliveryFee`), so points are only granted once payment completes.
**Stock deduction stays at placement** as a reservation (avoids overselling between
placement and payment); a future "cancel unpaid order" job can restock — out of
scope here.

## Files

- **New:** `Models/Entities/Payment.cs`, `Configuration/EsewaOptions.cs`,
  `Services/Interfaces/IEsewaPaymentService.cs`, `Services/EsewaPaymentService.cs`,
  `Controllers/PaymentsController.cs`, plus generated `Migrations/*_AddPayments.*`
- **Edit:** `Data/BlueberryMartDbContext.cs` (DbSet, enum, entity config),
  `Program.cs` (bind options, AddHttpClient, register service),
  `appsettings.json` (Esewa section),
  `Controllers/OrdersController.cs` (move loyalty credit out of placement)

## Verification

1. **Build + migrate:** `dotnet build BlueberryMart.Api`; run
   `dotnet run --project BlueberryMart.Api` and confirm the `AddPayments` migration
   applies on startup (new `payments` table + `payment_status` enum exist).
2. **Unit tests** (`Tests/BlueberryMart.Api.Tests`): add tests asserting the
   initiation signature matches a known eSewa sandbox vector for fixed inputs, and
   that `VerifyAndDecode` rejects a tampered payload. Run `dotnet test`.
3. **End-to-end (sandbox):**
   - `POST /api/payments/esewa/initiate` for a pending order → returns `formUrl` +
     `fields`; POST those to eSewa's test form in a browser.
   - Pay with eSewa sandbox test credentials → browser is redirected to
     `/api/payments/esewa/success` → verify payment row flips to `completed`, order
     to `confirmed`, loyalty points credited, and a 302 to the success deep link.
   - Repeat hitting `/failure` to confirm the payment is marked `failed` and the
     order stays `pending`.

## Notes / to confirm during implementation
- eSewa ePay-v2 endpoints, the sandbox secret `8gBm/:&EnhH.1/q`, and the
  `total_amount,transaction_uuid,product_code` signing string are taken from eSewa's
  developer docs — re-verify against current docs while implementing.
- Amounts are sent to eSewa as plain numbers; ensure `total_amount` formatting
  matches what the signature is computed over (no thousands separators).
