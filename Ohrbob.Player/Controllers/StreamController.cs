using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("stream")]
public class StreamController : ControllerBase
{
    // The widget script does not pass the Authorization header by default 
    // without the token being configured, but since it has a token in its config,
    // let's leave this open or simple for now so it doesn't fail if the token setup isn't perfect yet.
    // In a real app we'd add [Authorize] once the Player API verifies tokens via JWT.
    
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new 
        { 
            stationName = "Ohrbob Radio", 
            trackName = "Welcome to Ohrbob", 
            artistName = "Ohrbob", 
            isPlaying = false 
        });
    }

    [HttpPost("play")]
    public IActionResult Play()
    {
        return Ok();
    }

    [HttpPost("pause")]
    public IActionResult Pause()
    {
        return Ok();
    }

    [HttpPost("next")]
    public IActionResult Next()
    {
        return Ok();
    }

    [HttpPost("prev")]
    public IActionResult Prev()
    {
        return Ok();
    }
}