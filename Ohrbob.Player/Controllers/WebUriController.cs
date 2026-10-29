using Microsoft.AspNetCore.Mvc;
using Ohrbob.Player.Services;

namespace Ohrbob.Player.Controllers;

[ApiController]
[Route("/weburi")]
public class WebUriController : ControllerBase
{
    private readonly PlayerContextService _contextService;
    private readonly IConfiguration _configuration;

    public WebUriController(PlayerContextService contextService, IConfiguration configuration)
    {
        _contextService = contextService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<WebUriResponse>> Get()
    {
        if (await _contextService.GetProfileByStreamTokenAsync() == null)
            return Unauthorized();

        var webUri = _configuration["OhrbobPlayer:WebPublicUrl"];
        if (string.IsNullOrWhiteSpace(webUri))
            return Problem("Web frontend URI is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        return Ok(new WebUriResponse(webUri));
    }
}

public record WebUriResponse(string Uri);
