namespace Ohrbob.Api.Services;

/// <summary>
/// Polls feeds when their deduced schedule says new episodes are likely, instead of
/// fetching every feed on a fixed cadence. Feeds without a schedule (new feeds, or feeds
/// whose publish pattern is not clear yet) keep the historic hourly behaviour.
/// </summary>
public class FeedUpdateWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedUpdateWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);

    public FeedUpdateWorker(IServiceProvider serviceProvider, ILogger<FeedUpdateWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FeedUpdateWorker is starting.");

        // Wait a few seconds before the first run. This gives the host time to fully start
        // and prevents the worker from querying the database during build-time OpenAPI generation.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return; // Host shut down before the delay finished
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessFeedsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing FeedUpdateWorker.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("FeedUpdateWorker is stopping.");
    }

    private async Task ProcessFeedsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var feedsService = scope.ServiceProvider.GetRequiredService<FeedsService>();
        var updateService = scope.ServiceProvider.GetRequiredService<FeedUpdateService>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var feeds = await feedsService.GetAllAsync();

        foreach (var feed in feeds)
        {
            if (!FeedUpdateService.IsDue(feed, now)) continue;
            await updateService.RefreshAsync(feed.Id, cancellationToken);
        }
    }
}
