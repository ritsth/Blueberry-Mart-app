using System.Text.Json;
using Google.Cloud.BigQuery.V2;
using Npgsql;

// ---------------------------------------------------------------------------
// BlueberryMart.SeedGen — one-shot tool that fabricates ~3 years of realistic
// order history and loads it into the BigQuery `sales_fact` table (the wide,
// denormalized fact table that backs the self-service "Explore" analytics).
//
// Realism (not just volume) is the point: a growth trend, monthly seasonality
// (Dashain/Tihar + year-end spikes), weekend lift, member vs guest behavior,
// and a high-skewed review distribution — so the charts look alive.
//
//   dotnet run --project BlueberryMart.SeedGen -- --rows 200000 --seed 42
// ---------------------------------------------------------------------------

string GetArg(string name, string fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name)
            return args[i + 1];
    return fallback;
}

var targetRows = int.Parse(GetArg("--rows", "200000"));
var seed = int.Parse(GetArg("--seed", "42"));
var project = GetArg("--project", Environment.GetEnvironmentVariable("BQ_PROJECT") ?? "project-76ca6efe-7878-4dc8-bff");
var datasetId = GetArg("--dataset", "blueberrymart");
var tableId = GetArg("--table", "sales_fact");
var pgConn = GetArg("--pg", Environment.GetEnvironmentVariable("PG_CONN")
    ?? "Host=localhost;Port=5432;Database=blueberry_mart;Username=postgres;Password=ritsth");

var rng = new Random(seed);

Console.WriteLine($"Seed generator — target ~{targetRows:N0} order-lines, seed {seed}");
Console.WriteLine($"BigQuery: {project}.{datasetId}.{tableId}");

// --- 1. Read the live catalog from Postgres ---------------------------------
// Keeps branch/item/price names consistent with the real app; categories are
// derived from item names (the inventory table has no category column).
var items = new List<Item>();
await using (var conn = new NpgsqlConnection(pgConn))
{
    await conn.OpenAsync();

    var branchNames = new Dictionary<Guid, string>();
    await using (var cmd = new NpgsqlCommand("SELECT id, name FROM branches", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
            branchNames[r.GetGuid(0)] = r.GetString(1);

    await using (var cmd = new NpgsqlCommand(
        "SELECT branch_id, item_name, price, is_bulk_only FROM inventory", conn))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            var branch = branchNames[r.GetGuid(0)];
            var name = r.GetString(1);
            items.Add(new Item(branch, name, CategoryFor(name), r.GetDecimal(2), r.GetBoolean(3)));
        }
}

if (items.Count == 0)
{
    Console.Error.WriteLine("No inventory found in Postgres — is the local DB up and seeded?");
    return 1;
}

var byBranch = items.GroupBy(i => i.Branch).ToDictionary(g => g.Key, g => g.ToList());
var branches = byBranch.Keys.OrderBy(b => b).ToArray();
Console.WriteLine($"Loaded {items.Count} items across {branches.Length} branches; " +
                  $"{items.Select(i => i.Category).Distinct().Count()} categories.");

// Flagship gets the lion's share. First branch (alphabetically "Downtown") is the flagship.
var branchWeights = branches.Select((_, idx) => idx == 0 ? 0.62 : 0.38 / Math.Max(1, branches.Length - 1)).ToArray();

// --- 2. Synthetic customers -------------------------------------------------
const int customerCount = 4000;
var customers = new (string Id, bool Member)[customerCount];
for (var i = 0; i < customerCount; i++)
    customers[i] = (Guid.NewGuid().ToString(), rng.NextDouble() < 0.30); // ~30% members

// --- 3. Date span + demand model -------------------------------------------
var endDate = DateTime.UtcNow.Date.AddDays(-1);
var startDate = endDate.AddYears(-3).AddDays(1);
var days = new List<DateTime>();
for (var d = startDate; d <= endDate; d = d.AddDays(1)) days.Add(d);
var span = days.Count;

// Monthly seasonality (Nepal: Oct Dashain/Tihar peak, year-end lift, summer dip).
double[] monthMult = { 0.90, 0.85, 0.95, 1.00, 1.00, 0.90, 0.90, 0.95, 1.05, 1.35, 1.20, 1.30 };
double Season(DateTime d) => monthMult[d.Month - 1];
double Growth(int idx) => 0.55 + 0.90 * (idx / (double)Math.Max(1, span - 1)); // 0.55 -> 1.45
double Weekday(DateTime d) => d.DayOfWeek switch
{
    DayOfWeek.Saturday => 1.35,
    DayOfWeek.Sunday => 1.25,
    DayOfWeek.Friday => 1.15,
    _ => 1.00,
};

// Calibrate the base order rate so the run lands near the target row count.
const double avgLinesPerOrder = 2.95;
double sumWeights = 0;
for (var i = 0; i < span; i++) sumWeights += Growth(i) * Season(days[i]) * Weekday(days[i]);
var baseOrders = targetRows / avgLinesPerOrder / sumWeights;

// --- 4. Generate rows -> NDJSON --------------------------------------------
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var tmpPath = Path.Combine(Path.GetTempPath(), $"sales_fact_{DateTime.UtcNow:yyyyMMddHHmmss}.ndjson");

int[] hours = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };
double[] hourW = { 2, 3, 4, 4, 5, 4, 3, 3, 4, 6, 7, 6, 4, 2 };
int[] lineCounts = { 1, 2, 3, 4, 5, 6 };
double[] lineCountW = { 15, 30, 25, 15, 10, 5 };
int[] qtyVals = { 1, 2, 3, 4 };
double[] qtyW = { 45, 30, 15, 10 };
int[] bulkQty = { 1, 2 };
double[] bulkQtyW = { 70, 30 };

long orderNumber = 1001;
long rowsWritten = 0;
long orders = 0;

await using (var sw = new StreamWriter(tmpPath))
{
    foreach (var (day, idx) in days.Select((d, i) => (d, i)))
    {
        var noise = 0.85 + rng.NextDouble() * 0.30; // 0.85 .. 1.15
        var ordersToday = (int)Math.Round(baseOrders * Growth(idx) * Season(day) * Weekday(day) * noise);

        for (var o = 0; o < ordersToday; o++)
        {
            var branch = Pick(branches, branchWeights, rng);
            var (customerId, member) = customers[rng.Next(customerCount)];
            var orderType = rng.NextDouble() < 0.42 ? "delivery" : "pickup";
            var paymentRoll = rng.NextDouble();
            var paymentStatus = paymentRoll < 0.92 ? "completed" : paymentRoll < 0.97 ? "failed" : "initiated";
            var paid = paymentStatus == "completed";

            // Members can buy bulk; guests cannot.
            var eligible = byBranch[branch].Where(i => member || !i.IsBulk).ToList();
            if (eligible.Count == 0) continue;

            // Sample distinct line items (no repeats within an order).
            Shuffle(eligible, rng);
            var k = Math.Min(Pick(lineCounts, lineCountW, rng) + (member && rng.NextDouble() < 0.4 ? 1 : 0), eligible.Count);

            var orderId = Guid.NewGuid().ToString();
            var hour = Pick(hours, hourW, rng);
            var occurredAt = new DateTime(day.Year, day.Month, day.Day, hour, rng.Next(60), rng.Next(60), DateTimeKind.Utc);

            for (var li = 0; li < k; li++)
            {
                var item = eligible[li];
                var qty = item.IsBulk ? Pick(bulkQty, bulkQtyW, rng) : Pick(qtyVals, qtyW, rng);
                var lineRevenue = qty * item.Price;
                var discount = member ? Math.Round(lineRevenue * 0.05m, 2) : 0m;
                var firstLine = li == 0;
                var deliveryFee = firstLine && orderType == "delivery" && !member ? 100m : 0m;

                long? rating = null;
                var hasReview = false;
                if (paid && rng.NextDouble() < 0.22)
                {
                    hasReview = true;
                    var rr = rng.NextDouble();
                    rating = rr < 0.50 ? 5 : rr < 0.75 ? 4 : rr < 0.90 ? 3 : rr < 0.96 ? 2 : 1;
                }

                var row = new FactRow
                {
                    OrderId = orderId,
                    OrderLineId = Guid.NewGuid().ToString(),
                    OrderNumber = orderNumber,
                    OccurredAt = occurredAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    OrderDate = day.ToString("yyyy-MM-dd"),
                    Year = day.Year,
                    Month = day.Month,
                    YearMonth = day.ToString("yyyy-MM"),
                    DayOfWeek = day.DayOfWeek.ToString(),
                    Hour = hour,
                    BranchName = branch,
                    Category = item.Category,
                    ItemName = item.Name,
                    OrderType = orderType,
                    IsMember = member,
                    IsBulk = item.IsBulk,
                    PaymentStatus = paymentStatus,
                    CustomerId = customerId,
                    Quantity = qty,
                    UnitPrice = item.Price,
                    LineRevenue = lineRevenue,
                    DiscountAmount = discount,
                    DeliveryFee = deliveryFee,
                    Rating = rating,
                    HasReview = hasReview,
                    IsOrderPrimaryLine = firstLine,
                };

                await sw.WriteLineAsync(JsonSerializer.Serialize(row, jsonOpts));
                rowsWritten++;
            }

            orderNumber++;
            orders++;
            if (rowsWritten % 50_000 == 0 && rowsWritten > 0)
                Console.WriteLine($"  ...{rowsWritten:N0} lines");
        }
    }
}

Console.WriteLine($"Generated {rowsWritten:N0} lines across {orders:N0} orders -> {tmpPath}");

// --- 5. (Re)create the BigQuery table and load ------------------------------
var schema = new TableSchemaBuilder
{
    { "order_id", BigQueryDbType.String },
    { "order_line_id", BigQueryDbType.String },
    { "order_number", BigQueryDbType.Int64 },
    { "occurred_at", BigQueryDbType.Timestamp },
    { "order_date", BigQueryDbType.Date },
    { "year", BigQueryDbType.Int64 },
    { "month", BigQueryDbType.Int64 },
    { "year_month", BigQueryDbType.String },
    { "day_of_week", BigQueryDbType.String },
    { "hour", BigQueryDbType.Int64 },
    { "branch_name", BigQueryDbType.String },
    { "category", BigQueryDbType.String },
    { "item_name", BigQueryDbType.String },
    { "order_type", BigQueryDbType.String },
    { "is_member", BigQueryDbType.Bool },
    { "is_bulk", BigQueryDbType.Bool },
    { "payment_status", BigQueryDbType.String },
    { "customer_id", BigQueryDbType.String },
    { "quantity", BigQueryDbType.Int64 },
    { "unit_price", BigQueryDbType.Numeric },
    { "line_revenue", BigQueryDbType.Numeric },
    { "discount_amount", BigQueryDbType.Numeric },
    { "delivery_fee", BigQueryDbType.Numeric },
    { "rating", BigQueryDbType.Int64 },
    { "has_review", BigQueryDbType.Bool },
    { "is_order_primary_line", BigQueryDbType.Bool },
}.Build();

var client = BigQueryClient.Create(project);

try
{
    client.DeleteTable(datasetId, tableId);
    Console.WriteLine("Dropped existing sales_fact table.");
}
catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
{
    // first run — nothing to drop
}

client.CreateTable(datasetId, tableId, schema);
Console.WriteLine("Created sales_fact table.");

Console.WriteLine("Loading into BigQuery (load job, not streaming)...");
await using (var stream = File.OpenRead(tmpPath))
{
    var job = client.UploadJson(datasetId, tableId, schema, stream);
    var completed = job.PollUntilCompleted().ThrowOnAnyError();
    Console.WriteLine($"Load job complete: {completed.Reference.JobId}");
}

File.Delete(tmpPath);
Console.WriteLine($"Done. ~{rowsWritten:N0} rows in {project}.{datasetId}.{tableId}");
return 0;

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------
static string CategoryFor(string name)
{
    var n = name.ToLowerInvariant();
    if (Has(n, "spinach", "tomato", "lettuce", "veg", "fruit", "apple", "banana")) return "Produce";
    if (Has(n, "bread", "sourdough", "bun", "bagel", "pastry")) return "Bakery";
    if (Has(n, "milk", "yogurt", "cheese", "egg", "butter", "cream")) return "Dairy & Eggs";
    if (Has(n, "chicken", "meat", "fish", "beef", "pork", "mutton")) return "Meat & Poultry";
    if (Has(n, "rice", "lentil", "flour", "wheat", "bean", "pulse", "grain")) return "Grains & Pulses";
    if (Has(n, "oil")) return "Cooking Oil";
    if (Has(n, "juice", "water", "soda", "drink", "beverage")) return "Beverages";
    if (Has(n, "sugar", "salt", "spice")) return "Pantry";
    return "Other";
}

static bool Has(string haystack, params string[] needles)
{
    foreach (var w in needles)
        if (haystack.Contains(w)) return true;
    return false;
}

static T Pick<T>(T[] options, double[] weights, Random rng)
{
    double total = 0;
    foreach (var w in weights) total += w;
    var roll = rng.NextDouble() * total;
    for (var i = 0; i < options.Length; i++)
    {
        roll -= weights[i];
        if (roll <= 0) return options[i];
    }
    return options[^1];
}

static void Shuffle<T>(IList<T> list, Random rng)
{
    for (var i = list.Count - 1; i > 0; i--)
    {
        var j = rng.Next(i + 1);
        (list[i], list[j]) = (list[j], list[i]);
    }
}

internal readonly record struct Item(string Branch, string Name, string Category, decimal Price, bool IsBulk);

internal sealed class FactRow
{
    public string OrderId { get; set; } = "";
    public string OrderLineId { get; set; } = "";
    public long OrderNumber { get; set; }
    public string OccurredAt { get; set; } = "";
    public string OrderDate { get; set; } = "";
    public long Year { get; set; }
    public long Month { get; set; }
    public string YearMonth { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public long Hour { get; set; }
    public string BranchName { get; set; } = "";
    public string Category { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string OrderType { get; set; } = "";
    public bool IsMember { get; set; }
    public bool IsBulk { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public long Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineRevenue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DeliveryFee { get; set; }
    public long? Rating { get; set; }
    public bool HasReview { get; set; }
    public bool IsOrderPrimaryLine { get; set; }
}
