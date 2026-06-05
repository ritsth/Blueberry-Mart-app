using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Self-service analytics over the BigQuery <c>sales_fact</c> table: exposes an
/// introspected field catalog and runs validated, parameterized aggregation queries.
/// </summary>
public interface IAnalyticsQueryService
{
    /// <summary><c>false</c> when BigQuery isn't configured (e.g. production today).</summary>
    bool Enabled { get; }

    Task<AnalyticsCatalog> GetCatalogAsync(CancellationToken ct = default);

    Task<AnalyticsResult> RunQueryAsync(AnalyticsQueryRequest request, CancellationToken ct = default);
}

/// <summary>Thrown when a query spec fails validation against the catalog/guardrails.</summary>
public sealed class AnalyticsValidationException(string message) : Exception(message);
