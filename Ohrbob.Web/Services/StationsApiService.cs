using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Microsoft.Extensions.Logging;

namespace Ohrbob.Web.Services;

public class StationsApiService : OhrbobApiService
{
    public StationsApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options, IHttpClientFactory clientFactory) : base(logger, options, clientFactory)
    {
    }

    public virtual async Task<List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>?> GetStationsByAccountIdAsync(string accountId, string? profileId = null)
    {
        var path = $"/stations/account/{accountId}";
        if (!string.IsNullOrEmpty(profileId)) path += $"?profileId={profileId}";
        using var request = BuildRequest(HttpMethod.Get, path);
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>>();
    }

    public virtual async Task<Station?> GetStationByIdAsync(string id)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/stations/{id}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Station>();
    }

    public virtual async Task<Ohrbob.Server.Shared.Requests.StationWithFeedsDto?> GetStationDetailsByIdAsync(string id)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/stations/{id}/details");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>();
    }

    public virtual async Task<Station?> CreateStationAsync(string accountId, Station station)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{accountId}");
        request.Content = new StringContent(JsonSerializer.Serialize(station), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
                if (problem != null && problem.Title == "Limit exceeded")
                {
                    throw new LimitExceededException(problem.Detail ?? "Limit exceeded");
                }
            }
            catch (Exception ex) when (ex is not LimitExceededException)
            {
            }
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Station>();
    }

    public virtual async Task<Station?> UpdateStationAsync(Station station)
    {
        using var request = BuildRequest(HttpMethod.Put, $"/stations");
        request.Content = new StringContent(JsonSerializer.Serialize(station), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
                if (problem != null && problem.Title == "Limit exceeded")
                {
                    throw new LimitExceededException(problem.Detail ?? "Limit exceeded");
                }
            }
            catch (Exception ex) when (ex is not LimitExceededException)
            {
            }
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Station>();
    }

    public virtual async Task DeleteStationAsync(string stationId)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/stations/{stationId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task UpdateScheduleAsync(string stationId, StationSchedule schedule)
    {
        using var request = BuildRequest(HttpMethod.Put, $"/stations/{stationId}/schedule");
        request.Content = new StringContent(JsonSerializer.Serialize(schedule), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task<Ohrbob.Server.Shared.Requests.FeedImportResultDto?> AddFeedsAsync(string stationId, IEnumerable<string> urls)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/feeds");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new Ohrbob.Server.Shared.Requests.FeedImportRequest { Urls = urls.ToList() }),
            Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Ohrbob.Server.Shared.Requests.FeedImportResultDto>();
    }

    public virtual async Task<List<Ohrbob.Server.Shared.Requests.EpisodePlayStateDto>?> GetStationPlaylistAsync(string id, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/stations/{id}/playlist?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Ohrbob.Server.Shared.Requests.EpisodePlayStateDto>>();
    }

    public virtual async Task<List<Ohrbob.Server.Shared.Requests.EpisodePlayStateDto>?> GetStationCompletedPlaylistAsync(string id, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/stations/{id}/completed?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Ohrbob.Server.Shared.Requests.EpisodePlayStateDto>>();
    }

    public virtual async Task MarkEpisodeCompletedAsync(string stationId, string episodeId, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/episodes/{episodeId}/complete?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task MarkEpisodeCompletedUntilAsync(string stationId, string episodeId, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/episodes/{episodeId}/completeUntil?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task ClearEpisodeCompletedAsync(string stationId, string episodeId, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/episodes/{episodeId}/clear?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task SetEpisodePlayheadAsync(string stationId, string episodeId, string profileId, int positionSeconds)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/episodes/{episodeId}/playhead?profileId={profileId}&positionSeconds={positionSeconds}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task ClearEpisodeCompletedUntilAsync(string stationId, string episodeId, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/stations/{stationId}/episodes/{episodeId}/clearUntil?profileId={profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}

public class LimitExceededException : System.Exception
{
    public LimitExceededException(string message) : base(message) { }
}
