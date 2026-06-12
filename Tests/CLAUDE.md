# Tests/ — BlueberryMart.Api.Tests (xUnit integration tests)

Integration tests that boot the real API via `WebApplicationFactory<Program>` against a **real
local Postgres** (not in-memory, not Testcontainers).

## Running
- `dotnet test Tests/BlueberryMart.Api.Tests`
- Needs Postgres on `localhost:5432`, db `blueberry_mart_test`, user `postgres` / pass `ritsth`
  (see `Infrastructure/BlueberryMartApiFactory.cs`). CI provides it as a service container.
- The factory recreates the schema each run (`EnsureDeleted` → `DbInitializer` runs migrations +
  seeds), then exposes seeded ids (`DowntownBranchId`, `EggsItemId`, …).

## Conventions
- One xUnit collection `"Integration"` shares the factory across test classes → **shared DB**.
  Make assertions resilient to other tests' data (filter by ids you created; don't assume global
  counts). See `TestHelpers.DemoteOtherAdminsAsync` for the pattern.
- Reach the DB directly via `factory.Services.CreateScope()` →
  `GetRequiredService<BlueberryMartDbContext>()` (e.g. to assert an `outbox_messages` row, or
  `TestHelpers.SetOrderStatusAsync`).
- Common helpers (tokens, place order, create user/item) live in
  `Infrastructure/TestHelpers.cs`; auth tokens via `GetCustomerTokenAsync` etc.
- Kafka/BigQuery are **off** in tests (no `Kafka:BootstrapServers`), so the no-op producer is
  used and consumers/sinks aren't registered — but controllers still write `outbox_messages`
  rows (harmless; nothing dispatches them).
- CI format gate is on `BlueberryMart.Api` only — the test project isn't format-checked.
