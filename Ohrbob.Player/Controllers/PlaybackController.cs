using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Player.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaybackController : ControllerBase
{
    private readonly PlayerContextService _contextService;
    private readonly ListenStateService _stateService;
    private readonly PlaybackStrategyService _playbackStrategy;
    private readonly IMongoCollection<Station> _stations;
    private readonly IMongoCollection<Episode> _episodes;

    public PlaybackController(
        PlayerContextService contextService,
        ListenStateService stateService,
        PlaybackStrategyService playbackStrategy,
        MongoDbContext dbContext)
    {
        _contextService = contextService;
        _stateService = stateService;
        _playbackStrategy = playbackStrategy;
        _stations = dbContext.GetCollection<Station>("StationCollection");
        _episodes = dbContext.GetCollection<Episode>("EpisodeCollection");
    }

    [HttpGet("stations")]
    public async Task<IActionResult> GetStations()
    {
        var profile = await _contextService.GetCurrentProfileAsync();
        if (profile == null) return Forbid();
        
        var stationIds = (profile.AllowedStationIds ?? Array.Empty<string>())
            .Concat(profile.SubscribedStationIds ?? Array.Empty<string>())
            .Distinct()
            .ToArray();
        return Ok(stationIds);
    }

    [HttpGet("state/{episodeId}")]
    public async Task<IActionResult> GetState(string episodeId)
    {
        var profile = await _contextService.GetCurrentProfileAsync();
        if (profile == null) return Forbid();

        var state = await _stateService.GetStateAsync(profile.Id, episodeId);
        return Ok(new { PositionSeconds = state?.PositionSeconds ?? 0, IsListened = state?.IsListened ?? false });
    }

    [HttpPost("state/{episodeId}")]
    public async Task<IActionResult> SetState(string episodeId, [FromBody] SetStateRequest request)
    {
        var profile = await _contextService.GetCurrentProfileAsync();
        if (profile == null) return Forbid();

        if (request.IsListened)
        {
            var station = await _stations.Find(s => s.Id == request.StationId && s.DeletedAt == null).FirstOrDefaultAsync();
            var episode = await _episodes.Find(e => e.Id == episodeId).FirstOrDefaultAsync();
            if (station == null || episode == null) return NotFound();
            if (!await _playbackStrategy.IsEpisodePlayableAsync(station, episode, profile))
                return Forbid();
        }

        await _stateService.UpsertStateAsync(profile.Id, request.StationId, episodeId, request.PositionSeconds, request.IsListened);
        return Ok();
    }
}

public class SetStateRequest
{
    public string StationId { get; set; } = string.Empty;
    public int PositionSeconds { get; set; }
    public bool IsListened { get; set; }
}
