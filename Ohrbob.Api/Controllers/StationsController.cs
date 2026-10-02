using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private readonly StationsService _stationsService;
    private readonly FeedsService _feedsService;
    private readonly EpisodesService _episodesService;
    private readonly ListenStateService _listenStateService;
    private readonly ProfilesService _profilesService;

    public StationsController(StationsService stationsService, FeedsService feedsService, EpisodesService episodesService, ListenStateService listenStateService, ProfilesService profilesService)
    {
        _stationsService = stationsService;
        _feedsService = feedsService;
        _episodesService = episodesService;
        _listenStateService = listenStateService;
        _profilesService = profilesService;
    }
    [EndpointSummary("Get all stations by accountId")]
    [EndpointDescription(@"Returns a list of all stations associated with an accountId")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("account/{accountId}")]
    public async Task<ActionResult<List<StationWithFeedsDto>>> GetByAccountId(string accountId)
    {
        var stations = await _stationsService.GetByAccountIdAsync(accountId);
        var dtos = new List<StationWithFeedsDto>();

        foreach (var station in stations)
        {
            var dto = new StationWithFeedsDto { Station = station };
            if (station.Feeds != null && station.Feeds.Any())
            {
                foreach (var url in station.Feeds)
                {
                    var feed = await _feedsService.FindByUrlAsync(url);
                    var stats = new FeedStatsDto { Url = url };
                    if (feed != null)
                    {
                        stats.Title = feed.Title;
                        stats.LastUpdated = feed.LastUpdated;
                        stats.ImageUrl = feed.ImageUrl;
                    
                        
                        stats.EpisodeCount = await _episodesService.CountByFeedIdAsync(feed.Id);
                    }
                    dto.Feeds.Add(stats);
                }
            }
            dtos.Add(dto);
        }
        return Ok(dtos);
    }
    
    [EndpointSummary("Get station by id")]
    [EndpointDescription(@"Returns a station by its id")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("{id}")]
    public async Task<ActionResult<Station>> GetById(string id)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null)
            return NotFound();
        return Ok(station);
    }

    [EndpointSummary("Get station details with feed stats")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("{id}/details")]
    public async Task<ActionResult<StationWithFeedsDto>> GetDetailsById(string id)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null) return NotFound();

        var dto = new StationWithFeedsDto { Station = station };
        
        if (station.Feeds != null)
        {
            foreach (var url in station.Feeds)
            {
                var feed = await _feedsService.FindByUrlAsync(url);
                var stats = new FeedStatsDto { Url = url };
                
                if (feed != null)
                {
                    stats.Title = feed.Title;
                    stats.LastUpdated = feed.LastUpdated;
                        stats.ImageUrl = feed.ImageUrl;
                    
                    stats.EpisodeCount = await _episodesService.CountByFeedIdAsync(feed.Id);
                }
                dto.Feeds.Add(stats);
            }
        }
        
        return Ok(dto);
    }

    [EndpointSummary("Get station playlist")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("{id}/playlist")]
    public async Task<ActionResult<List<EpisodePlayStateDto>>> GetPlaylist(string id, [FromQuery] string profileId)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null || station.Feeds == null || !station.Feeds.Any()) 
            return NotFound();

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null) return NotFound();

        var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
        var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId);
        
        var listenedIds = listenStates.Where(s => s.IsListened).Select(s => s.EpisodeId).ToHashSet();
        
        var orderedEpisodes = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(
            episodes, station.Strategy, listenedIds, profile.LastActiveEpisodeId);

        var result = new List<EpisodePlayStateDto>();
        foreach(var ep in orderedEpisodes)
        {
            var state = listenStates.FirstOrDefault(s => s.EpisodeId == ep.Id);
            result.Add(new EpisodePlayStateDto
            {
                Episode = ep,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsActive = profile.LastActiveEpisodeId == ep.Id
            });
        }
        
        // Limit to 50 for UI performance
        return Ok(result.Take(50).ToList());
    }

    private async Task<bool> ValidateAndEnsureFeedsExistAsync(string[]? feedUrls)
    {
        if (feedUrls == null || feedUrls.Length == 0) return true;

        foreach (var url in feedUrls)
        {
            var existingFeed = await _feedsService.FindByUrlAsync(url);
            if (existingFeed == null)
            {
                var newFeed = await _feedsService.ValidateAndParseFeedAsync(url);
                if (newFeed == null)
                {
                    return false;
                }
                await _feedsService.CreateAsync(newFeed);
            }
        }
        return true;
    }

    [EndpointSummary("Create station for accountId")]
    [EndpointDescription(@"Creates station and returns it.")]
    [Authorize(policy: Consts.CreateStationPolicy)]
    [HttpPost("{accountId}")]
    public async Task<IActionResult> Create(string accountId, [FromBody] Station request)
    {
        if (!await ValidateAndEnsureFeedsExistAsync(request.Feeds))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Feed", Detail = "One or more feeds could not be validated as RSS/Atom." });
        }

        var station = new Station
        {
            AccountId = accountId,
            Name = request.Name,
            Strategy = request.Strategy,
            IsPublic = request.IsPublic,
            Feeds = request.Feeds
        };
        var createdStation = await _stationsService.CreateAsync(station);
        return Ok(createdStation);
    }

    [EndpointSummary("Update station")]
    [Authorize(policy:Consts.UpdateStationPolicy)]
    [HttpPut()]
    public async Task<IActionResult> Update([FromBody] Station station)
    {
        if (!await ValidateAndEnsureFeedsExistAsync(station.Feeds))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Feed", Detail = "One or more feeds could not be validated as RSS/Atom." });
        }

        var updatedStation = await _stationsService.UpdateAsync(station);
        if (updatedStation == null)
        {
            return NotFound();
        }

        return Ok(updatedStation);
    }
    
    [EndpointSummary("Delete station")]
    [EndpointDescription(@"Delete station given its id.")]
    [Authorize(policy:Consts.DeleteStationPolicy)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _stationsService.DeleteAsync(id);
        if (!result)
        {
            return NotFound();
        }
        return Ok();
    }
}
