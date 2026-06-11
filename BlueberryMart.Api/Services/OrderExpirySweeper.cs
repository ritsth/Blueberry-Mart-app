using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Periodically releases stock from unpaid orders. Runs only on the always-on worker
/// (one instance) — never on the autoscaling API — so there's a single sweeper and it
/// never disappears when the API scales to zero.
/// </summary>
public sealed class OrderExpirySweeper(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<OrderExpirySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hold window is configurable so it can be shortened for demos
        // (e.g. Orders__HoldMinutes=2 on the worker). Sweep cadence adapts to short holds.
        var holdMinutes = Math.Max(1, config.GetValue("Orders:HoldMinutes", 30));
        var holdWindow = TimeSpan.FromMinutes(holdMinutes);
        var interval = TimeSpan.FromSeconds(Math.Min(60, holdMinutes * 30));

        logger.LogInformation("OrderExpirySweeper started ({Hold}-min hold, sweeping every {Interval}s)",
            holdMinutes, (int)interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IOrderExpiryService>();
                await svc.SweepExpiredAsync(holdWindow, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Order expiry sweep failed; will retry next tick");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
