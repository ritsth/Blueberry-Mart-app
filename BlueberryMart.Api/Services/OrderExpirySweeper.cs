using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Periodically releases stock from unpaid orders. Runs only on the always-on worker
/// (one instance) — never on the autoscaling API — so there's a single sweeper and it
/// never disappears when the API scales to zero.
/// </summary>
public sealed class OrderExpirySweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderExpirySweeper> logger) : BackgroundService
{
    private static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OrderExpirySweeper started ({Hold}-min hold, every {Interval}s)",
            (int)HoldWindow.TotalMinutes, (int)Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IOrderExpiryService>();
                await svc.SweepExpiredAsync(HoldWindow, stoppingToken);
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
