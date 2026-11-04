using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedsService _feedsService;

    public FeedsController(FeedsService feedsService)
    {
        _feedsService = feedsService;
    }

    [EndpointSummary("Get all feeds")]
    [EndpointDescription("Returns every feed that has been added to a station. Used by Discover recommendations.")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet]
    public async Task<ActionResult<List<Feed>>> GetAll()
    {
        return Ok(await _feedsService.GetAllAsync());
    }
}
