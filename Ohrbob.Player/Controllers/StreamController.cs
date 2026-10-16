using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Player.Services;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;
using MongoDB.Driver;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("stream")]
public class StreamController : ControllerBase
{
    private readonly PlayerContextService _contextService;
    private readonly ListenStateService _listenStateService;
    private readonly PlaybackStrategyService _playbackStrategy;
    private readonly IMongoCollection<Station> _stations;
    private readonly IMongoCollection<Episode> _episodes;
    private readonly IMongoCollection<Profile> _profiles;
    private readonly IMongoCollection<Feed> _feeds;

    public StreamController(
        PlayerContextService contextService, 
        ListenStateService listenStateService, 
        PlaybackStrategyService playbackStrategy,
        MongoDbContext dbContext)
    {
        _contextService = contextService;
        _listenStateService = listenStateService;
        _playbackStrategy = playbackStrategy;
        _stations = dbContext.GetCollection<Station>("StationCollection");
        _episodes = dbContext.GetCollection<Episode>("EpisodeCollection");
        _profiles = dbContext.GetCollection<Profile>("ProfileCollection");
        _feeds = dbContext.GetCollection<Feed>("FeedCollection");
    }

    private async Task<Profile?> GetProfile()
    {
        return await _contextService.GetProfileByStreamTokenAsync();
    }

    [HttpGet("stations")]
    public async Task<IActionResult> GetStations()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        var allowedStationIds = profile.AllowedStationIds ?? Array.Empty<string>();
        var stations = await _stations.Find(s => allowedStationIds.Contains(s.Id)).ToListAsync();

        
        var stationIds = stations.Select(s => s.Id).ToList();
        var allFeedUrls = stations.SelectMany(s => s.Feeds ?? Array.Empty<string>()).Distinct().ToList();
        var allFeeds = await _feeds.Find(f => allFeedUrls.Contains(f.Url)).ToListAsync();

        var dtos = stations.Select(s => new StationWithFeedsDto
        {
            Station = s,
            Feeds = s.Feeds?.Select(fUrl => 
            {
                var f = allFeeds.FirstOrDefault(x => x.Url == fUrl);
                return new FeedStatsDto { Url = fUrl, ImageUrl = f?.ImageUrl };
            }).ToList() ?? new System.Collections.Generic.List<FeedStatsDto>()
        });

        return Ok(dtos);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        string? activeStationId = profile.LastActiveStationId;
        string? activeEpisodeId = profile.LastActiveEpisodeId;
        
        // Fallback if no history
        if (string.IsNullOrEmpty(activeStationId) && profile.AllowedStationIds?.Length > 0)
        {
            activeStationId = profile.AllowedStationIds[0];
        }
        
        if (string.IsNullOrEmpty(activeStationId)) return Ok(new { });

        var station = await _stations.Find(s => s.Id == activeStationId && s.DeletedAt == null).FirstOrDefaultAsync();
        if (station == null) return Ok(new { });

        Episode? currentEpisode = null;
        if (!string.IsNullOrEmpty(activeEpisodeId))
        {
             currentEpisode = await _episodes.Find(e => e.Id == activeEpisodeId).FirstOrDefaultAsync();
             // Ignore stale episodes that do not belong to the active station (e.g. left over from a deleted station)
             if (currentEpisode != null && !station.Feeds.Contains(currentEpisode.FeedUrl))
             {
                 currentEpisode = null;
             }
        }
        
        if (currentEpisode == null)
        {
             currentEpisode = await _playbackStrategy.GetNextEpisodeAsync(station, null, profile.Id);
             if (currentEpisode != null) activeEpisodeId = currentEpisode.Id;
        }

        if (currentEpisode == null)
        {
            var feeds = await _feeds.Find(f => station.Feeds.Contains(f.Url)).ToListAsync();
            bool isCompiling = station.Feeds.Length > 0 && (!feeds.Any() || feeds.Any(f => !f.LastUpdated.HasValue));

            return Ok(new 
            {
                stationId = station.Id,
                stationName = station.Name,
                isCompiling = isCompiling,
                isEmpty = !isCompiling
            });
        }

        var listenState = await _listenStateService.GetStateAsync(profile.Id, currentEpisode.Id);
        int position = listenState?.PositionSeconds ?? 0;

        var nextEpisode = await _playbackStrategy.GetNextEpisodeAsync(station, currentEpisode.Id, profile.Id);
        var prevEpisode = await _playbackStrategy.GetPreviousEpisodeAsync(station, currentEpisode.Id, profile.Id);

        // Resolve feed titles for artist display (feed title fallback to station name)
        var stationFeeds = await _feeds.Find(f => station.Feeds.Contains(f.Url)).ToListAsync();
        string ResolveArtist(Episode ep) =>
            stationFeeds.FirstOrDefault(f => f.Url == ep.FeedUrl || f.Id == ep.FeedId)?.Title
            ?? station.Name;

        return Ok(new 
        { 
            stationId = station.Id,
            stationName = station.Name, 
            current = new {
                episodeId = currentEpisode.Id,
                title = currentEpisode.Title,
                description = currentEpisode.Description,
                artist = ResolveArtist(currentEpisode),
                coverUrl = string.IsNullOrWhiteSpace(currentEpisode.ImageUrl) ? null : currentEpisode.ImageUrl,
                streamUri = currentEpisode.AudioUrl, // Using AudioUrl from Episode
                link = string.IsNullOrWhiteSpace(currentEpisode.AudioUrl) ? null : currentEpisode.AudioUrl,
                playheadPositionSeconds = position
            },
            next = nextEpisode == null ? null : new {
                episodeId = nextEpisode.Id,
                title = nextEpisode.Title,
                description = nextEpisode.Description,
                artist = ResolveArtist(nextEpisode),
                coverUrl = string.IsNullOrWhiteSpace(nextEpisode.ImageUrl) ? null : nextEpisode.ImageUrl,
                streamUri = nextEpisode.AudioUrl,
                link = string.IsNullOrWhiteSpace(nextEpisode.AudioUrl) ? null : nextEpisode.AudioUrl
            },
            prev = prevEpisode == null ? null : new {
                episodeId = prevEpisode.Id,
                title = prevEpisode.Title,
                description = prevEpisode.Description,
                artist = ResolveArtist(prevEpisode),
                coverUrl = string.IsNullOrWhiteSpace(prevEpisode.ImageUrl) ? null : prevEpisode.ImageUrl,
                streamUri = prevEpisode.AudioUrl,
                link = string.IsNullOrWhiteSpace(prevEpisode.AudioUrl) ? null : prevEpisode.AudioUrl
            }
        });
    }

    [HttpPost("status")]
    public async Task<IActionResult> SyncStatus([FromBody] SyncStatusRequest req)
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        await _listenStateService.UpsertStateAsync(profile.Id, req.StationId, req.EpisodeId, req.PositionSeconds, false);
        
        if (profile.LastActiveStationId != req.StationId || profile.LastActiveEpisodeId != req.EpisodeId)
        {
             var update = Builders<Profile>.Update
                .Set(p => p.LastActiveStationId, req.StationId)
                .Set(p => p.LastActiveEpisodeId, req.EpisodeId);
             await _profiles.UpdateOneAsync(p => p.Id == profile.Id, update);
        }

        return Ok();
    }

    [HttpPost("play-station/{stationId}")]
    public async Task<IActionResult> PlayStation(string stationId)
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        var station = await _stations.Find(s => s.Id == stationId && s.DeletedAt == null).FirstOrDefaultAsync();
        if (station == null) return NotFound();

        // Clear active episode to let the strategy pick the next one based on the new station
        var update = Builders<Profile>.Update
            .Set(p => p.LastActiveStationId, stationId)
            .Set(p => p.LastActiveEpisodeId, null);
            
        await _profiles.UpdateOneAsync(p => p.Id == profile.Id, update);
        
        return Ok();
    }

    [HttpPost("next")]
    public async Task<IActionResult> Next()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();
        
        if (string.IsNullOrEmpty(profile.LastActiveStationId)) return BadRequest();
        var station = await _stations.Find(s => s.Id == profile.LastActiveStationId && s.DeletedAt == null).FirstOrDefaultAsync();
        if (station == null) return BadRequest();

        // Current episode has been fully listened
        if (!string.IsNullOrEmpty(profile.LastActiveEpisodeId))
        {
            await _listenStateService.UpsertStateAsync(profile.Id, station.Id, profile.LastActiveEpisodeId, 0, true);
        }

        var nextEpisode = await _playbackStrategy.GetNextEpisodeAsync(station, profile.LastActiveEpisodeId, profile.Id);
        if (nextEpisode == null) return Ok();

        var update = Builders<Profile>.Update.Set(p => p.LastActiveEpisodeId, nextEpisode.Id);
        await _profiles.UpdateOneAsync(p => p.Id == profile.Id, update);
        
        // Ensure state is 0 for next
        await _listenStateService.UpsertStateAsync(profile.Id, station.Id, nextEpisode.Id, 0, false);

        return Ok();
    }

    [HttpPost("prev")]
    public async Task<IActionResult> Prev()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();
        
        if (string.IsNullOrEmpty(profile.LastActiveStationId)) return BadRequest();
        var station = await _stations.Find(s => s.Id == profile.LastActiveStationId && s.DeletedAt == null).FirstOrDefaultAsync();
        if (station == null) return BadRequest();

        var prevEpisode = await _playbackStrategy.GetPreviousEpisodeAsync(station, profile.LastActiveEpisodeId, profile.Id);
        if (prevEpisode == null) return Ok();

        var update = Builders<Profile>.Update.Set(p => p.LastActiveEpisodeId, prevEpisode.Id);
        await _profiles.UpdateOneAsync(p => p.Id == profile.Id, update);
        
        // Ensure state is 0 for prev so it plays from the start and isn't skipped as listened
        await _listenStateService.UpsertStateAsync(profile.Id, station.Id, prevEpisode.Id, 0, false);

        return Ok();
    }
}

public class SyncStatusRequest
{
    public string EpisodeId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public int PositionSeconds { get; set; }
}
