using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Services.Interfaces;
using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>Runs analytics queries against the BigQuery <c>stock_events</c> table.</summary>
public sealed class BigQueryInventoryAnalytics : IInventoryAnalytics
{
    private readonly BigQueryOptions _opts;
    private readonly BigQueryClient _client;

    public bool Enabled => true;

    public BigQueryInventoryAnalytics(IOptions<BigQueryOptions> opts)
    {
        _opts = opts.Value;
        _client = BigQueryClient.Create(_opts.ProjectId);
    }

    public async Task<IReadOnlyList<StockMovementRow>> StockMovementByReasonAsync(CancellationToken ct = default)
    {
        var sql =
            $"SELECT reason, COUNT(*) AS events, SUM(new_quantity - old_quantity) AS net_change " +
            $"FROM `{_opts.ProjectId}.{_opts.DatasetId}.{_opts.TableId}` " +
            $"GROUP BY reason ORDER BY events DESC";

        var results = await _client.ExecuteQueryAsync(sql, parameters: null, cancellationToken: ct);

        var rows = new List<StockMovementRow>();
        foreach (var row in results)
        {
            rows.Add(new StockMovementRow(
                (string)row["reason"],
                Convert.ToInt64(row["events"]),
                row["net_change"] is null ? 0 : Convert.ToInt64(row["net_change"])));
        }
        return rows;
    }
}

/// <summary>Used when BigQuery isn't configured — analytics report as disabled.</summary>
public sealed class DisabledInventoryAnalytics : IInventoryAnalytics
{
    public bool Enabled => false;

    public Task<IReadOnlyList<StockMovementRow>> StockMovementByReasonAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StockMovementRow>>(Array.Empty<StockMovementRow>());
}
