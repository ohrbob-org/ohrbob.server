using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Player.Services;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;
using MongoDB.Driver;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("stream")]
public class StreamController : ControllerBase
{
    private readonly PlayerContextService _contextService;
    private readonly ListenStateService _listenStateService;
    private readonly IMongoCollection<Station> _stations;
    private readonly IMongoCollection<Episode> _episodes;

    // Simple static in-memory state to simulate playback state
    private static readonly Dictionary<string, bool> _playingState = new();

    public StreamController(
        PlayerContextService contextService, 
        ListenStateService listenStateService, 
        MongoDbContext dbContext)
    {
        _contextService = contextService;
        _listenStateService = listenStateService;
        _stations = dbContext.GetCollection<Station>("StationCollection");
        _episodes = dbContext.GetCollection<Episode>("EpisodeCollection");
    }

    private async Task<Profile?> GetProfile()
    {
        return await _contextService.GetProfileByStreamTokenAsync();
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        var lastState = await _listenStateService.GetLastStateAsync(profile.Id);

        string stationName = "Ohrbob Radio";
        string trackName = "Welcome to Ohrbob";
        string artistName = "Ohrbob";
        string? coverUrl = null;

        if (lastState != null)
        {
            var station = await _stations.Find(s => s.Id == lastState.StationId).FirstOrDefaultAsync();
            if (station != null) stationName = station.Name;

            var episode = await _episodes.Find(e => e.Id == lastState.EpisodeId).FirstOrDefaultAsync();
            if (episode != null)
            {
                trackName = episode.Title;
            }
        }
        else if (profile.AllowedStationIds?.Length > 0)
        {
            var firstStation = await _stations.Find(s => s.Id == profile.AllowedStationIds[0]).FirstOrDefaultAsync();
            if (firstStation != null) stationName = firstStation.Name;
        }

        _playingState.TryGetValue(profile.Id, out bool isPlaying);

        return Ok(new 
        { 
            stationName, 
            trackName, 
            artistName, 
            coverUrl,
            isPlaying 
        });
    }

    [HttpPost("play")]
    public async Task<IActionResult> Play()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        _playingState[profile.Id] = true;
        return Ok();
    }

    [HttpPost("pause")]
    public async Task<IActionResult> Pause()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        _playingState[profile.Id] = false;
        return Ok();
    }

    [HttpPost("next")]
    public async Task<IActionResult> Next()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();
        return Ok();
    }

    [HttpPost("prev")]
    public async Task<IActionResult> Prev()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();
        return Ok();
    }
}