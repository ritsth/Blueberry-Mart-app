using System.Globalization;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;
using BlueberryMart.Api.Services.Interfaces;
using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Self-service analytics over the BigQuery <c>sales_fact</c> table.
///
/// The "broad but safe" design: the field catalog is built by <b>introspecting the
/// table schema</b> (so it's wide and auto-growing), and a query is a spec of
/// <b>catalog IDs</b>. Every identifier that reaches the SQL string is resolved from
/// the server-side catalog; only filter <i>values</i> come from the client, and those
/// are always bound as <see cref="BigQueryParameter"/>s. No client string is ever
/// concatenated into SQL.
/// </summary>
public sealed class BigQueryAnalyticsQueryService : IAnalyticsQueryService
{
    private readonly BigQueryOptions _opts;
    private readonly BigQueryClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Cached after first introspection (the table schema is static for a process).
    private AnalyticsCatalog? _catalog;
    private Dictionary<string, BigQueryDbType> _columnTypes = new();
    private Dictionary<string, MeasureDef> _measures = new();
    private HashSet<string> _dimensionIds = new();

    public bool Enabled => true;

    public BigQueryAnalyticsQueryService(IOptions<BigQueryOptions> opts)
    {
        _opts = opts.Value;
        _client = BigQueryClient.Create(_opts.ProjectId);
    }

    private string TableRef => $"`{_opts.ProjectId}.{_opts.DatasetId}.{_opts.SalesFactTableId}`";

    // --- semantic layer (the handful of exceptions to pure introspection) ---------
    private static readonly HashSet<string> Hidden =
        new() { "order_id", "order_line_id", "customer_id", "order_number", "occurred_at", "is_order_primary_line" };

    // Numeric columns that are really categorical → expose as dimensions, not measures.
    private static readonly HashSet<string> NumericDimensions = new() { "year", "month", "hour" };

    private static readonly string[] DefaultNumericAggs = { "sum", "avg", "min", "max" };

    private static readonly Dictionary<string, string[]> MeasureAggs = new()
    {
        ["line_revenue"] = new[] { "sum", "avg", "max" },
        ["quantity"] = new[] { "sum", "avg", "max" },
        ["discount_amount"] = new[] { "sum", "avg" },
        ["delivery_fee"] = new[] { "sum" }, // 0 on non-primary lines, so SUM at line grain is correct
        ["unit_price"] = new[] { "avg", "min", "max" },
        ["rating"] = new[] { "avg", "min", "max" },
    };

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["order_date"] = "Date",
        ["year"] = "Year",
        ["month"] = "Month",
        ["year_month"] = "Year-Month",
        ["day_of_week"] = "Day of week",
        ["hour"] = "Hour",
        ["branch_name"] = "Branch",
        ["category"] = "Category",
        ["item_name"] = "Item",
        ["order_type"] = "Order type",
        ["is_member"] = "Member",
        ["is_bulk"] = "Bulk item",
        ["payment_status"] = "Payment status",
        ["has_review"] = "Has review",
        ["line_revenue"] = "Revenue",
        ["quantity"] = "Quantity",
        ["discount_amount"] = "Discount",
        ["delivery_fee"] = "Delivery fee",
        ["unit_price"] = "Unit price",
        ["rating"] = "Rating",
    };

    /// <summary>A measure's underlying column, the aggs it permits, and its label.</summary>
    private sealed record MeasureDef(string Column, string[] Aggs, string Label);

    // ------------------------------------------------------------------------------
    public async Task<AnalyticsCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        await EnsureCatalogAsync(ct);
        return _catalog!;
    }

    private async Task EnsureCatalogAsync(CancellationToken ct)
    {
        if (_catalog is not null) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_catalog is not null) return;

            var sql =
                $"SELECT column_name, data_type FROM `{_opts.ProjectId}.{_opts.DatasetId}`.INFORMATION_SCHEMA.COLUMNS " +
                "WHERE table_name = @t ORDER BY ordinal_position";
            var results = await _client.ExecuteQueryAsync(
                sql, new[] { new BigQueryParameter("t", BigQueryDbType.String, _opts.SalesFactTableId) },
                cancellationToken: ct);

            var columnTypes = new Dictionary<string, BigQueryDbType>();
            var dimensions = new List<CatalogDimension>();
            var measures = new List<CatalogMeasure>();
            var measureDefs = new Dictionary<string, MeasureDef>();

            foreach (var row in results)
            {
                var name = (string)row["column_name"];
                var bqType = MapType((string)row["data_type"]);
                columnTypes[name] = bqType;

                if (Hidden.Contains(name)) continue;

                var numeric = IsNumeric(bqType);
                if (numeric && !NumericDimensions.Contains(name))
                {
                    var aggs = MeasureAggs.GetValueOrDefault(name, DefaultNumericAggs);
                    measureDefs[name] = new MeasureDef(name, aggs, Label(name));
                    measures.Add(new CatalogMeasure(name, Label(name), aggs));
                }
                else
                {
                    dimensions.Add(new CatalogDimension(name, Label(name), DataTypeLabel(bqType)));
                }
            }

            // Synthetic count measures (the things you can't express as a column agg).
            AddSynthetic(measureDefs, measures, "orders", "order_id", "count_distinct", "Orders");
            AddSynthetic(measureDefs, measures, "lines", "*", "count", "Order lines");
            AddSynthetic(measureDefs, measures, "customers", "customer_id", "count_distinct", "Customers");

            _columnTypes = columnTypes;
            _measures = measureDefs;
            _dimensionIds = dimensions.Select(d => d.Id).ToHashSet();
            _catalog = new AnalyticsCatalog(true, dimensions, measures);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void AddSynthetic(
        Dictionary<string, MeasureDef> defs, List<CatalogMeasure> catalog,
        string id, string column, string agg, string label)
    {
        defs[id] = new MeasureDef(column, new[] { agg }, label);
        catalog.Add(new CatalogMeasure(id, label, new[] { agg }));
    }

    // ------------------------------------------------------------------------------
    public async Task<AnalyticsResult> RunQueryAsync(AnalyticsQueryRequest request, CancellationToken ct = default)
    {
        await EnsureCatalogAsync(ct);

        if (request.Measures.Count == 0)
            throw new AnalyticsValidationException("Pick at least one measure.");
        if (request.Measures.Count > 6)
            throw new AnalyticsValidationException("At most 6 measures.");
        if (request.Dimensions.Count > 3)
            throw new AnalyticsValidationException("At most 3 dimensions.");

        var hasDateFilter = request.Filters.Any(f =>
            f.Field is "order_date" or "year" or "year_month");
        if (!hasDateFilter)
            throw new AnalyticsValidationException(
                "A date filter on order_date, year, or year_month is required.");

        var limit = Math.Clamp(request.Limit ?? 1000, 1, 5000);

        var select = new List<string>();
        var groupBy = new List<string>();
        var columns = new List<AnalyticsColumn>();
        var aliases = new HashSet<string>();

        // dimensions (columns map directly — the date grains are precomputed columns)
        foreach (var dim in request.Dimensions)
        {
            if (!_dimensionIds.Contains(dim))
                throw new AnalyticsValidationException($"Unknown dimension '{dim}'.");
            if (!aliases.Add(dim)) continue;
            select.Add($"{dim} AS {dim}");
            groupBy.Add(dim);
            columns.Add(new AnalyticsColumn(dim, Label(dim), "dimension"));
        }

        // measures
        foreach (var m in request.Measures)
        {
            if (!_measures.TryGetValue(m.Field, out var def))
                throw new AnalyticsValidationException($"Unknown measure '{m.Field}'.");
            var agg = m.Agg.ToLowerInvariant();
            if (!def.Aggs.Contains(agg))
                throw new AnalyticsValidationException($"Aggregation '{agg}' not allowed on '{m.Field}'.");

            var alias = $"{m.Field}_{agg}";
            if (!aliases.Add(alias)) continue;
            select.Add($"{AggExpr(agg, def.Column)} AS {alias}");
            columns.Add(new AnalyticsColumn(alias, $"{def.Label} ({AggLabel(agg)})", "measure"));
        }

        // filters → WHERE with bound parameters
        var parameters = new List<BigQueryParameter>();
        var where = BuildWhere(request.Filters, parameters);

        var sql = $"SELECT {string.Join(", ", select)} FROM {TableRef} WHERE {where}";
        if (groupBy.Count > 0)
            sql += " GROUP BY " + string.Join(", ", groupBy);

        if (request.OrderBy is { Count: > 0 })
        {
            var orderParts = new List<string>();
            foreach (var o in request.OrderBy)
            {
                if (!aliases.Contains(o.Field))
                    throw new AnalyticsValidationException(
                        $"Order-by '{o.Field}' must be a selected dimension or measure.");
                var dir = o.Dir.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
                orderParts.Add($"{o.Field} {dir}");
            }
            sql += " ORDER BY " + string.Join(", ", orderParts);
        }

        sql += $" LIMIT {limit}";

        var queryResults = await _client.ExecuteQueryAsync(sql, parameters, cancellationToken: ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in queryResults)
        {
            var dict = new Dictionary<string, object?>(columns.Count);
            foreach (var c in columns)
                dict[c.Key] = Normalize(row[c.Key]);
            rows.Add(dict);
        }

        return new AnalyticsResult(columns, rows);
    }

    // ------------------------------------------------------------------------------
    private string BuildWhere(IEnumerable<FilterSpec> filters, List<BigQueryParameter> parameters)
    {
        var clauses = new List<string>();
        var p = 0;

        foreach (var f in filters)
        {
            if (!_columnTypes.TryGetValue(f.Field, out var type))
                throw new AnalyticsValidationException($"Unknown filter field '{f.Field}'.");

            var op = f.Op.ToLowerInvariant();
            string Add(string raw)
            {
                var name = $"p{p++}";
                parameters.Add(new BigQueryParameter(name, type, ConvertValue(raw, type)));
                return "@" + name;
            }

            switch (op)
            {
                case "eq":
                    clauses.Add($"{f.Field} = {Add(One(f))}");
                    break;
                case "ne":
                    clauses.Add($"{f.Field} != {Add(One(f))}");
                    break;
                case "gt":
                    clauses.Add($"{f.Field} > {Add(One(f))}");
                    break;
                case "gte":
                    clauses.Add($"{f.Field} >= {Add(One(f))}");
                    break;
                case "lt":
                    clauses.Add($"{f.Field} < {Add(One(f))}");
                    break;
                case "lte":
                    clauses.Add($"{f.Field} <= {Add(One(f))}");
                    break;
                case "in":
                    if (f.Values.Count == 0)
                        throw new AnalyticsValidationException($"Filter '{f.Field}' IN needs values.");
                    clauses.Add($"{f.Field} IN ({string.Join(", ", f.Values.Select(Add))})");
                    break;
                case "between":
                    if (f.Values.Count != 2)
                        throw new AnalyticsValidationException($"Filter '{f.Field}' BETWEEN needs 2 values.");
                    clauses.Add($"{f.Field} BETWEEN {Add(f.Values[0])} AND {Add(f.Values[1])}");
                    break;
                case "contains":
                    if (type != BigQueryDbType.String)
                        throw new AnalyticsValidationException($"'contains' only applies to text fields.");
                    var name = $"p{p++}";
                    parameters.Add(new BigQueryParameter(name, BigQueryDbType.String, $"%{One(f)}%"));
                    clauses.Add($"{f.Field} LIKE @{name}");
                    break;
                default:
                    throw new AnalyticsValidationException($"Unsupported operator '{f.Op}'.");
            }
        }

        return clauses.Count > 0 ? string.Join(" AND ", clauses) : "TRUE";
    }

    private static string One(FilterSpec f) =>
        f.Values.Count > 0 ? f.Values[0]
            : throw new AnalyticsValidationException($"Filter '{f.Field}' needs a value.");

    private static string AggExpr(string agg, string column) => agg switch
    {
        "count" => "COUNT(*)",
        "count_distinct" => $"COUNT(DISTINCT {column})",
        "sum" => $"SUM({column})",
        "avg" => $"AVG({column})",
        "min" => $"MIN({column})",
        "max" => $"MAX({column})",
        _ => throw new AnalyticsValidationException($"Unsupported aggregation '{agg}'."),
    };

    private static string AggLabel(string agg) => agg switch
    {
        "count_distinct" => "distinct",
        _ => agg,
    };

    private static object ConvertValue(string raw, BigQueryDbType type) => type switch
    {
        BigQueryDbType.String => raw,
        BigQueryDbType.Bool => bool.Parse(raw),
        BigQueryDbType.Int64 => long.Parse(raw, CultureInfo.InvariantCulture),
        BigQueryDbType.Float64 => double.Parse(raw, CultureInfo.InvariantCulture),
        BigQueryDbType.Numeric => BigQueryNumeric.Parse(raw),
        BigQueryDbType.BigNumeric => BigQueryBigNumeric.Parse(raw),
        BigQueryDbType.Date => DateTime.Parse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date,
        BigQueryDbType.Timestamp => DateTime.Parse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        _ => raw,
    };

    private static object? Normalize(object? v) => v switch
    {
        null => null,
        BigQueryNumeric n => decimal.Parse(n.ToString(), CultureInfo.InvariantCulture),
        BigQueryBigNumeric n => decimal.Parse(n.ToString(), CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        _ => v,
    };

    private static bool IsNumeric(BigQueryDbType t) =>
        t is BigQueryDbType.Int64 or BigQueryDbType.Numeric
            or BigQueryDbType.Float64 or BigQueryDbType.BigNumeric;

    private static BigQueryDbType MapType(string dataType) => dataType.ToUpperInvariant() switch
    {
        "STRING" => BigQueryDbType.String,
        "INT64" or "INTEGER" => BigQueryDbType.Int64,
        "NUMERIC" => BigQueryDbType.Numeric,
        "BIGNUMERIC" => BigQueryDbType.BigNumeric,
        "FLOAT64" or "FLOAT" => BigQueryDbType.Float64,
        "BOOL" or "BOOLEAN" => BigQueryDbType.Bool,
        "DATE" => BigQueryDbType.Date,
        "TIMESTAMP" => BigQueryDbType.Timestamp,
        _ => BigQueryDbType.String,
    };

    private static string DataTypeLabel(BigQueryDbType t) => t switch
    {
        BigQueryDbType.Int64 or BigQueryDbType.Numeric or BigQueryDbType.Float64 or BigQueryDbType.BigNumeric => "number",
        BigQueryDbType.Bool => "bool",
        BigQueryDbType.Date => "date",
        BigQueryDbType.Timestamp => "timestamp",
        _ => "string",
    };

    private static string Label(string column) =>
        Labels.GetValueOrDefault(column, Humanize(column));

    private static string Humanize(string column)
    {
        var words = column.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((w, i) =>
            i == 0 && w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }
}

/// <summary>Used when BigQuery isn't configured — the Explore feature reports disabled.</summary>
public sealed class DisabledAnalyticsQueryService : IAnalyticsQueryService
{
    public bool Enabled => false;

    public Task<AnalyticsCatalog> GetCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(new AnalyticsCatalog(false,
            Array.Empty<CatalogDimension>(), Array.Empty<CatalogMeasure>()));

    public Task<AnalyticsResult> RunQueryAsync(AnalyticsQueryRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("Analytics warehouse is not configured.");
}
