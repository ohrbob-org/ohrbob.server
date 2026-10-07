using System.Text.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public class VersionPollingService(
    IHttpClientFactory clientFactory,
    IOptions<OhrbobApiOptions> options,
    BackendVersionCache cache,
    ILogger<VersionPollingService> logger) : BackgroundService
{
    public const string ApiClientName = "version-api";
    public const string PlayerClientName = "version-player";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        await PollOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollOnceAsync(stoppingToken);
    }

    public async Task PollOnceAsync(CancellationToken ct)
    {
        var api = await ProbeAsync(ApiClientName, "Api", ct);
        var player = await ProbeAsync(PlayerClientName, "Player", ct);
        cache.Update(api.Version, api.Tag, api.Reachable, player.Version, player.Tag, player.Reachable, DateTime.UtcNow);
    }

    private async Task<(string Version, string Tag, bool Reachable)> ProbeAsync(string clientName, string label, CancellationToken ct)
    {
        try
        {
            var client = clientFactory.CreateClient(clientName);
            using var response = await client.GetAsync(clientName == ApiClientName ? "/_health" : "/health", ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            return (root.GetProperty("version").GetString() ?? "unknown",
                    root.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "",
                    true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Version probe for {Label} failed", label);
            return ("unknown", "", false);
        }
    }
}
