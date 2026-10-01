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

    public PlaybackController(PlayerContextService contextService)
    {
        _contextService = contextService;
    }

    [HttpGet("stations")]
    public async Task<IActionResult> GetStations()
    {
        var profile = await _contextService.GetCurrentProfileAsync();
        if (profile == null) return Forbid();
        
        return Ok(profile.AllowedStationIds ?? Array.Empty<string>());
    }
}
