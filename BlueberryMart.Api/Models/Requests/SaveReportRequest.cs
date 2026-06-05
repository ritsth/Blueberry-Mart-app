using System.Text.Json;

namespace BlueberryMart.Api.Models.Requests;

/// <summary>
/// Save (or update) an "Explore" chart. <see cref="Config"/> is the opaque layout
/// schema — the query spec + chart type — stored as-is and replayed against fresh
/// data when the report is opened. The data itself is never persisted.
/// </summary>
public class SaveReportRequest
{
    public string Name { get; set; } = "";

    /// <summary>The chart configuration (query spec + chartType), stored verbatim as jsonb.</summary>
    public JsonElement Config { get; set; }
}
