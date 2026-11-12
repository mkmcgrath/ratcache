namespace RatCache.Web.Services;

// FR-3.5: expired pastes must be permanently deleted within 1 hour of expiring.
// Runs PasteService.PurgeExpiredAsync once at startup and then every PurgeIntervalMinutes
// (default 10, so the worst case is well inside the 1 hour limit).
// Set RatCache:PurgeIntervalMinutes to 0 to turn it off (the test factory does this).
public sealed class ExpiredPasteCleaner(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<ExpiredPasteCleaner> logger) : BackgroundService
{
    public const string IntervalConfigKey = "RatCache:PurgeIntervalMinutes";
    public const int DefaultIntervalMinutes = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = config.GetValue(IntervalConfigKey, DefaultIntervalMinutes);
        if (minutes <= 0)
        {
            logger.LogInformation("Expired-paste cleanup is disabled ({Key} = {Minutes}).", IntervalConfigKey, minutes);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        do
        {
            await PurgeOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task PurgeOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var pastes = scope.ServiceProvider.GetRequiredService<PasteService>();
            var removed = await pastes.PurgeExpiredAsync();
            // Log only the count, never paste content or titles (NFR-1.4).
            if (removed > 0) logger.LogInformation("Deleted {Count} expired paste(s).", removed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A failed run must not stop the app; the next tick tries again.
            logger.LogError(ex, "Expired-paste cleanup failed.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try { return await timer.WaitForNextTickAsync(stoppingToken); }
        catch (OperationCanceledException) { return false; }
    }
}