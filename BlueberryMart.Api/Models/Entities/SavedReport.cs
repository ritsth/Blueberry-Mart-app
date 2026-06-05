namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// A shareholder's saved "Explore" chart. Stores the <b>configuration</b> (the query
/// spec + chart type) as JSON — never the data — so opening it re-runs against fresh
/// numbers. See <c>AnalyticsController</c>.
/// </summary>
public class SavedReport
{
    public Guid Id { get; set; }
    public Guid ShareholderId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>The query spec + chartType, stored as a <c>jsonb</c> document.</summary>
    public string ConfigJson { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User Shareholder { get; set; } = null!;
}
