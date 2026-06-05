namespace BlueberryMart.Api.Models.Requests;

/// <summary>
/// A self-service analytics query expressed as a spec of <b>catalog IDs</b>, never
/// raw SQL. The server resolves every field/agg against the introspected catalog and
/// builds parameterized BigQuery SQL — so no client string is ever interpolated into
/// the query (filter values become bound parameters).
/// </summary>
public class AnalyticsQueryRequest
{
    /// <summary>At least one. Each picks a catalog measure + an aggregation.</summary>
    public List<MeasureSpec> Measures { get; set; } = new();

    /// <summary>Group-by dimensions (catalog dimension IDs). Capped server-side.</summary>
    public List<string> Dimensions { get; set; } = new();

    /// <summary>Filters; must include at least one date filter (order_date / year / year_month).</summary>
    public List<FilterSpec> Filters { get; set; } = new();

    public List<OrderSpec>? OrderBy { get; set; }

    public int? Limit { get; set; }

    /// <summary>Hint for the frontend renderer (bar/line/pie/table). Not used by SQL.</summary>
    public string? ChartType { get; set; }
}

public class MeasureSpec
{
    /// <summary>Catalog measure ID, e.g. <c>line_revenue</c>, <c>orders</c>.</summary>
    public string Field { get; set; } = "";

    /// <summary>Aggregation, e.g. <c>sum</c>, <c>avg</c>, <c>count_distinct</c>.</summary>
    public string Agg { get; set; } = "";
}

public class FilterSpec
{
    public string Field { get; set; } = "";

    /// <summary>eq, ne, gt, gte, lt, lte, in, between, contains.</summary>
    public string Op { get; set; } = "";

    public List<string> Values { get; set; } = new();
}

public class OrderSpec
{
    /// <summary>References a selected dimension ID or a measure result key (e.g. <c>line_revenue_sum</c>).</summary>
    public string Field { get; set; } = "";

    /// <summary>asc | desc.</summary>
    public string Dir { get; set; } = "desc";
}
