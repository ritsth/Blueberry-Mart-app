# Database/ — legacy raw SQL (history only)

⚠️ **Not the source of truth.** Schema is managed by **EF Core migrations** in
`BlueberryMart.Api/Migrations/`, which apply automatically on app startup
(`DbInitializer.Initialize`). These hand-written `.sql` files (`01_InitSchema.sql` …,
`Migrations/`, `Seeds/`) predate the EF setup and are kept only for history/reference.

- To change the schema: edit an entity in `BlueberryMart.Api/Models/Entities/`, then
  `dotnet dotnet-ef migrations add <Name> --project BlueberryMart.Api --output-dir Migrations`.
  **Do not** add new `.sql` files here expecting them to run.
- `01_InitSchema.sql` is still the readable reference for the Postgres `ENUM` types
  (`user_role`, `order_type`, `order_status`, `payment_status`) and overall shape.
