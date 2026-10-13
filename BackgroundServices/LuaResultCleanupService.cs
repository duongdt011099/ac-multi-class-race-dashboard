using MulticlassRace.Services.Results;

namespace MulticlassRace.BackgroundServices;

/// <summary>
/// Runs <see cref="LuaResultSweeper"/> at startup and hourly thereafter. Startup covers results left
/// behind while the dashboard was not running, which is when they accumulate unnoticed.
/// </summary>
internal sealed class LuaResultCleanupService(
    LuaResultSweeper sweeper,
    ILogger<LuaResultCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LuaResultSweeper.PollInterval);

        do
        {
            try
            {
                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The Lua result sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
