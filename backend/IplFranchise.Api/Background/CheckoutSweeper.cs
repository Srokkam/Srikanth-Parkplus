using IplFranchise.Application;

namespace IplFranchise.Api.Background;

/// <summary>
/// Finishes orders whose stock was deducted but whose order record was never written because the process stopped
/// in between. Safe to run on every instance: creating an order is create-if-absent.
/// </summary>
internal sealed class CheckoutSweeper(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CheckoutSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Sweeper:IntervalSeconds", 30));
        var grace = TimeSpan.FromSeconds(configuration.GetValue("Sweeper:GraceSeconds", 120));

        using var timer = new PeriodicTimer(interval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var recovery = scope.ServiceProvider.GetRequiredService<CheckoutRecovery>();
                var completed = await recovery.CompleteStaleAsync(grace, stoppingToken);
                if (completed > 0)
                {
                    logger.LogWarning("Completed {Count} orders left unfinished by an interrupted checkout", completed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Checkout sweep failed; it will run again");
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
