using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Server.Shared.Helpers;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private static readonly string[] StationFeedColors = new[]
    {
        "#E63946", // Imperial Red
        "#1D3557", // Prussian Blue
        "#457B9D", // Celadon Blue
        "#2A9D8F", // Persian Green
        "#E9C46A", // Sandy Brown
        "#F4A261", // Sandy Orange
        "#E76F51", // Burnt Sienna
        "#6A057F", // Grape Purple
        "#264653", // Charcoal Dark
        "#8AB17D", // Sage Green
        "#D62828", // Vibrant Red
        "#FF9F1C"  // Bright Orange
    };

    private readonly StationsService _stationsService;
    private readonly FeedsService _feedsService;
    private readonly EpisodesService _episodesService;
    private readonly ListenStateService _listenStateService;
    private readonly ProfilesService _profilesService;
    private readonly AccountService? _accountService;
    private readonly SubscriptionPolicy? _policy;

    public StationsController(StationsService stationsService, FeedsService feedsService, EpisodesService episodesService, ListenStateService listenStateService, ProfilesService profilesService, AccountService? accountService = null, SubscriptionPolicy? policy = null)
    {
        _stationsService = stationsService;
        _feedsService = feedsService;
        _episodesService = episodesService;
        _listenStateService = listenStateService;
        _profilesService = profilesService;
        _accountService = accountService;
        _policy = policy;
    }
    [EndpointSummary("Get all stations by accountId")]
    [EndpointDescription(@"Returns a list of all stations associated with an accountId. When a profileId is supplied, each station also reports how many episodes are still left to play for that profile (issue #6).")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("account/{accountId}")]
    public async Task<ActionResult<List<StationWithFeedsDto>>> GetByAccountId(string accountId, [FromQuery] string? profileId = null)
    {
        var stations = await _stationsService.GetByAccountIdAsync(accountId);
        var dtos = new List<StationWithFeedsDto>();

        // Completion lives in ListenState (per profile), so it can only be derived when a
        // profile is supplied. Load the profile's completed episodes once, up front.
        HashSet<string> completedEpisodeIds = new();
        var hasProfile = !string.IsNullOrEmpty(profileId);
        if (hasProfile)
        {
            var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId!);
            completedEpisodeIds = listenStates.Where(s => s.IsListened).Select(s => s.EpisodeId).ToHashSet();
        }

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

                if (hasProfile)
                {
                    // Same rule the player uses: an episode counts as unavailable once it is
                    // completed or aged out, so this matches the player's "nothing to play" state.
                    var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
                    var unavailable = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, completedEpisodeIds);
                    dto.UnplayedEpisodeCount = episodes.Count(e => !unavailable.Contains(e.Id));
                }
            }
            else if (hasProfile)
            {
                dto.UnplayedEpisodeCount = 0;
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
        listenedIds = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listenedIds);
        
        // Pass empty HashSet to GetOrderedPlaylist so we compute order of ALL episodes without filtering completed ones
        var orderedEpisodes = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(
            episodes, station, new HashSet<string>(), profile.LastActiveEpisodeId);

        var activeEpisodeId = profile.LastActiveEpisodeId;
        if (string.IsNullOrEmpty(activeEpisodeId) && profile.LastActiveStationId == id)
        {
            var firstUnplayed = orderedEpisodes.FirstOrDefault(e => !listenedIds.Contains(e.Id));
            if (firstUnplayed != null)
            {
                activeEpisodeId = firstUnplayed.Id;
            }
        }

        var result = new List<EpisodePlayStateDto>();
        foreach(var ep in orderedEpisodes)
        {
            var state = listenStates.FirstOrDefault(s => s.EpisodeId == ep.Id);
            result.Add(new EpisodePlayStateDto
            {
                Episode = ep,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsActive = activeEpisodeId == ep.Id,
                IsCompleted = listenedIds.Contains(ep.Id)
            });
        }

        // Return the full ordered playlist. The Edit view lists every episode of the
        // station (completed ones marked as such), so capping the result here truncates
        // large feeds (see issue #4).
        return Ok(result);
    }

    [EndpointSummary("Get station completed playlist")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("{id}/completed")]
    public async Task<ActionResult<List<EpisodePlayStateDto>>> GetCompletedPlaylist(string id, [FromQuery] string profileId)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null || station.Feeds == null || !station.Feeds.Any()) 
            return NotFound();

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null) return NotFound();

        var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
        var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId);
        
        var listenedIds = listenStates.Where(s => s.IsListened).Select(s => s.EpisodeId).ToHashSet();
        listenedIds = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listenedIds);
        
        // Order all episodes via PlaybackStrategyHelper (treating all as unplayed), then filter for completed ones
        var fullOrderedSequence = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(
            episodes, station, new HashSet<string>(), null);
        
        var completedEpisodes = fullOrderedSequence.Where(e => listenedIds.Contains(e.Id)).ToList();

        var result = new List<EpisodePlayStateDto>();
        foreach(var ep in completedEpisodes)
        {
            var state = listenStates.FirstOrDefault(s => s.EpisodeId == ep.Id);
            result.Add(new EpisodePlayStateDto
            {
                Episode = ep,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsActive = false
            });
        }
        
        return Ok(result.Take(50).ToList());
    }

    [EndpointSummary("Mark episode as completed for a profile")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpPost("{id}/episodes/{episodeId}/complete")]
    public async Task<IActionResult> MarkEpisodeCompleted(string id, string episodeId, [FromQuery] string profileId)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null) return NotFound();

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null) return NotFound();

        var state = await _listenStateService.GetStateAsync(profileId, episodeId);
        if (state == null)
        {
            state = new ListenState
            {
                UserId = profileId,
                EpisodeId = episodeId,
                StationId = id,
                IsListened = true
            };
            await _listenStateService.CreateAsync(state);
        }
        else
        {
            state.IsListened = true;
            await _listenStateService.UpdateAsync(state);
        }

        await ReevaluatePlayheadAsync(id, profileId);
        return Ok();
    }

    [EndpointSummary("Clear episode completed state for a profile")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpPost("{id}/episodes/{episodeId}/clear")]
    public async Task<IActionResult> ClearEpisodeCompleted(string id, string episodeId, [FromQuery] string profileId)
    {
        var state = await _listenStateService.GetStateAsync(profileId, episodeId);
        if (state != null)
        {
            state.IsListened = false;
            await _listenStateService.UpdateAsync(state);
        }
        await ReevaluatePlayheadAsync(id, profileId);
        return Ok();
    }

    [EndpointSummary("Clear episode completed state until for a profile")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpPost("{id}/episodes/{episodeId}/clearUntil")]
    public async Task<IActionResult> ClearEpisodeCompletedUntil(string id, string episodeId, [FromQuery] string profileId)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null || station.Feeds == null || !station.Feeds.Any()) 
            return NotFound();

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null) return NotFound();

        var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
        var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId);
        
        var fullOrderedSequence = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(
            episodes, station, new HashSet<string>(), null);

        foreach (var ep in fullOrderedSequence)
        {
            var state = listenStates.FirstOrDefault(s => s.EpisodeId == ep.Id);
            if (state != null)
            {
                state.IsListened = false;
                await _listenStateService.UpdateAsync(state);
            }

            if (ep.Id == episodeId)
            {
                break;
            }
        }

        await ReevaluatePlayheadAsync(id, profileId);
        return Ok();
    }

    private async Task ReevaluatePlayheadAsync(string stationId, string profileId)
    {
        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null || profile.LastActiveStationId != stationId) return;

        var station = await _stationsService.GetAsync(stationId);
        if (station == null || station.Feeds == null || !station.Feeds.Any()) return;

        var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
        var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId);
        var listenedIds = listenStates.Where(s => s.IsListened).Select(s => s.EpisodeId).ToHashSet();
        listenedIds = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listenedIds);

        var orderedPlaylist = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, new HashSet<string>(), null);
        var firstIncomplete = orderedPlaylist.FirstOrDefault(e => !listenedIds.Contains(e.Id));

        if (profile.LastActiveEpisodeId != (firstIncomplete?.Id))
        {
            profile.LastActiveEpisodeId = firstIncomplete?.Id;
            await _profilesService.UpdateAsync(profile);
        }
    }

    private static void AssignFeedColors(Station station)
    {
        for (int i = 0; i < station.Schedule.Feeds.Count; i++)
        {
            var feed = station.Schedule.Feeds[i];
            if (string.IsNullOrEmpty(feed.Color))
            {
                var assignedColors = station.Schedule.Feeds
                    .Select(f => f.Color)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToHashSet();

                var availableColor = StationFeedColors.FirstOrDefault(c => !assignedColors.Contains(c));
                feed.Color = availableColor ?? StationFeedColors[i % StationFeedColors.Length];
            }
        }
    }

    private async Task<bool> ValidateAndEnsureFeedsExistAsync(Station station)
    {
        if (station.Schedule == null) station.Schedule = new StationSchedule();
        if (station.Schedule.Feeds == null) station.Schedule.Feeds = new List<StationFeedSchedule>();

        // Keep backward compatibility: if Feeds is populated but Schedule is empty, migrate to schedule
        if (station.Feeds != null && station.Feeds.Any() && !station.Schedule.Feeds.Any())
        {
            foreach(var feedUrl in station.Feeds)
            {
                station.Schedule.Feeds.Add(new StationFeedSchedule { Url = feedUrl });
            }
        }

        // Assign colors sequentially to feeds that do not have one
        AssignFeedColors(station);
        
        // Sync Schedule back to Feeds array for backward compatibility with PlaybackStrategyHelper
        station.Feeds = station.Schedule.Feeds.Select(f => f.Url).ToArray();

        if (station.Feeds.Length == 0) return true;

        foreach (var url in station.Feeds)
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
        if (request.Schedule == null) request.Schedule = new StationSchedule();
        if (request.Schedule.Feeds == null) request.Schedule.Feeds = new List<StationFeedSchedule>();

        if (_accountService != null && _policy != null)
        {
            var account = await _accountService.GetAsync(accountId);
            var limits = _policy.For(account);
            var existing = await _stationsService.GetByAccountIdAsync(accountId);
            if (!_policy.CanCreateStation(existing.Count, limits))
                return StatusCode(403, new ProblemDetails { Status = 403, Title = "Limit exceeded", Detail = $"Station limit reached for {(account?.Tier ?? SubscriptionTier.Free)} tier." });
            if (!_policy.CanSetFeedCount(request.Schedule.Feeds.Count, limits))
                return StatusCode(403, new ProblemDetails { Status = 403, Title = "Limit exceeded", Detail = "Feed limit per station exceeded." });
        }

        if (!await ValidateAndEnsureFeedsExistAsync(request))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Feed", Detail = "One or more feeds could not be validated as RSS/Atom." });
        }

        var station = new Station
        {
            AccountId = accountId,
            Name = request.Name,
            IsPublic = request.IsPublic,
            Feeds = request.Feeds,
            Schedule = request.Schedule
        };
        var createdStation = await _stationsService.CreateAsync(station);
        return Ok(createdStation);
    }

    [EndpointSummary("Add feeds to an existing station")]
    [EndpointDescription(@"Adds the given feed URLs to the station, skipping feeds that are already present or cannot be read and stopping at the station's feed limit. Returns what was added, skipped or invalid.")]
    [Authorize(policy: Consts.UpdateStationPolicy)]
    [HttpPost("{id}/feeds")]
    public async Task<ActionResult<FeedImportResultDto>> AddFeeds(string id, [FromBody] FeedImportRequest request)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null) return NotFound();

        if (station.Schedule == null) station.Schedule = new StationSchedule();
        if (station.Schedule.Feeds == null) station.Schedule.Feeds = new List<StationFeedSchedule>();

        // Keep backward compatibility: migrate a legacy Feeds array into the schedule.
        if (station.Feeds != null && station.Feeds.Any() && !station.Schedule.Feeds.Any())
        {
            foreach (var feedUrl in station.Feeds)
                station.Schedule.Feeds.Add(new StationFeedSchedule { Url = feedUrl });
        }

        var result = new FeedImportResultDto();
        var stationUrls = station.Schedule.Feeds.Select(f => f.Url).ToHashSet();
        var requestedUrls = new HashSet<string>();

        int? maxFeeds = null;
        if (_accountService != null && _policy != null)
        {
            var account = await _accountService.GetAsync(station.AccountId);
            var limits = _policy.For(account);
            if (!TierLimits.IsUnlimited(limits.MaxFeedsPerStation))
                maxFeeds = limits.MaxFeedsPerStation;
        }

        foreach (var rawUrl in request?.Urls ?? new List<string>())
        {
            var url = rawUrl?.Trim();
            if (string.IsNullOrEmpty(url)) continue;

            // Duplicates inside the request are ignored silently.
            if (!requestedUrls.Add(url)) continue;

            if (stationUrls.Contains(url))
            {
                result.AlreadyPresent.Add(url);
                continue;
            }

            if (maxFeeds.HasValue && station.Schedule.Feeds.Count >= maxFeeds.Value)
            {
                result.NotAddedDueToLimit.Add(url);
                continue;
            }

            var feed = await _feedsService.FindByUrlAsync(url);
            if (feed == null)
            {
                feed = await _feedsService.ValidateAndParseFeedAsync(url);
                if (feed == null)
                {
                    result.Invalid.Add(url);
                    continue;
                }
                await _feedsService.CreateAsync(feed);
            }

            station.Schedule.Feeds.Add(new StationFeedSchedule { Url = url });
            stationUrls.Add(url);
            result.Added.Add(url);
        }

        station.Feeds = station.Schedule.Feeds.Select(f => f.Url).ToArray();
        AssignFeedColors(station);
        await _stationsService.UpdateAsync(station);

        return Ok(result);
    }

    [EndpointSummary("Update station schedule only")]
    [Authorize(policy:Consts.UpdateStationPolicy)]
    [HttpPut("{id}/schedule")]
    public async Task<IActionResult> UpdateSchedule(string id, [FromBody] StationSchedule schedule)
    {
        var existingStation = await _stationsService.GetAsync(id);
        if (existingStation == null) return NotFound();
        
        existingStation.Schedule = schedule;
        if (!await ValidateAndEnsureFeedsExistAsync(existingStation))
            return BadRequest(new ProblemDetails { Title = "Invalid Feed" });
            
        await _stationsService.UpdateAsync(existingStation);
        return Ok(existingStation);
    }
    
    [EndpointSummary("Mark episode as completed until")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpPost("{id}/episodes/{episodeId}/completeUntil")]
    public async Task<IActionResult> MarkEpisodeCompletedUntil(string id, string episodeId, [FromQuery] string profileId)
    {
        var station = await _stationsService.GetAsync(id);
        if (station == null || station.Feeds == null || !station.Feeds.Any()) 
            return NotFound();

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null) return NotFound();

        var episodes = await _episodesService.FindByFeedUrlsAsync(station.Feeds);
        var listenStates = await _listenStateService.GetStatesByUserIdAsync(profileId);
        
        var listenedIds = listenStates.Where(s => s.IsListened).Select(s => s.EpisodeId).ToHashSet();
        listenedIds = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listenedIds);
        
        var orderedEpisodes = Ohrbob.Server.Shared.Helpers.PlaybackStrategyHelper.GetOrderedPlaylist(
            episodes, station, listenedIds, profile.LastActiveEpisodeId);

        foreach (var ep in orderedEpisodes)
        {
            var state = listenStates.FirstOrDefault(s => s.EpisodeId == ep.Id);
            if (state == null)
            {
                state = new ListenState
                {
                    UserId = profileId,
                    EpisodeId = ep.Id,
                    StationId = id,
                    IsListened = true
                };
                await _listenStateService.CreateAsync(state);
            }
            else
            {
                state.IsListened = true;
                await _listenStateService.UpdateAsync(state);
            }

            if (ep.Id == episodeId)
            {
                break;
            }
        }

        await ReevaluatePlayheadAsync(id, profileId);
        return Ok();
    }

    [EndpointSummary("Update station")]
    [Authorize(policy:Consts.UpdateStationPolicy)]
    [HttpPut()]
    public async Task<IActionResult> Update([FromBody] Station station)
    {
        if (station.Schedule == null) station.Schedule = new StationSchedule();
        if (station.Schedule.Feeds == null) station.Schedule.Feeds = new List<StationFeedSchedule>();

        if (_accountService != null && _policy != null)
        {
            var existingStation = await _stationsService.GetAsync(station.Id);
            if (existingStation == null)
                return NotFound();
            var updateAccount = await _accountService.GetAsync(existingStation.AccountId);
            var updateLimits = _policy.For(updateAccount);
            if (!_policy.CanSetFeedCount(station.Schedule.Feeds.Count, updateLimits))
                return StatusCode(403, new ProblemDetails { Status = 403, Title = "Limit exceeded", Detail = "Feed limit per station exceeded." });
        }

        if (!await ValidateAndEnsureFeedsExistAsync(station))
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
        var station = await _stationsService.GetAsync(id);
        if (station == null) return NotFound();

        var result = await _stationsService.DeleteAsync(id);
        if (!result)
        {
            return NotFound();
        }

        // Clean up references in profiles
        var profiles = await _profilesService.GetByAccountIdAsync(station.AccountId);
        foreach (var profile in profiles)
        {
            bool modified = false;
            if (profile.AllowedStationIds != null && profile.AllowedStationIds.Contains(id))
            {
                profile.AllowedStationIds = profile.AllowedStationIds.Where(s => s != id).ToArray();
                modified = true;
            }
            if (profile.LastActiveStationId == id)
            {
                profile.LastActiveStationId = null;
                profile.LastActiveEpisodeId = null;
                modified = true;
            }
            
            if (modified)
            {
                await _profilesService.UpdateAsync(profile);
            }
        }

        return Ok();
    }
}
