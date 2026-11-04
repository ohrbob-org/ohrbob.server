namespace Ohrbob.Web.Services;

/// <summary>Warms the recommendation cache at startup so the first Discover request is fast.</summary>
public class RecommendationWarmupWorker : BackgroundService
{
    private readonly RecommendationService _recommendationService;
    private readonly ILogger<RecommendationWarmupWorker> _logger;

    public RecommendationWarmupWorker(RecommendationService recommendationService, ILogger<RecommendationWarmupWorker> logger)
    {
        _recommendationService = recommendationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _recommendationService.WarmUpAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recommendation warmup failed; lanes will fall back to local feeds.");
        }
    }
}
