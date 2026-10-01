using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Player.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlaybackController : ControllerBase
{
    private readonly PlayerContextService _contextService;
    private readonly ListenStateService _stateService;

    public PlaybackController(PlayerContextService contextService, ListenStateService stateService)
    {
        _contextService = contextService;
        _stateService = stateService;
    }

    [HttpGet("stations")]
    public async Task<IActionResult> GetStations()
    {
        var profile = await _contextService.GetCurrentProfileAsync();
        if (profile == null) return Forbid();
        
        return Ok(profile.AllowedStationIds ?? Array.Empty<string>());
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
