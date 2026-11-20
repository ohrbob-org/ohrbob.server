using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedsController : ControllerBase
{
    private readonly FeedsService _feedsService;
    private readonly FeedUpdateService _feedUpdateService;

    public FeedsController(FeedsService feedsService, FeedUpdateService feedUpdateService)
    {
        _feedsService = feedsService;
        _feedUpdateService = feedUpdateService;
    }

    [EndpointSummary("Get all feeds")]
    [EndpointDescription("Returns every feed that has been added to a station. Used by Discover recommendations.")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet]
    public async Task<ActionResult<List<Feed>>> GetAll()
    {
        return Ok(await _feedsService.GetAllAsync());
    }

    [EndpointSummary("Refresh a feed now")]
    [EndpointDescription("Fetches the feed immediately, updates its metadata and episodes, and reschedules its next automatic refresh. Returns the refreshed feed's stats.")]
    [Authorize(policy: Consts.UpdateStationPolicy)]
    [HttpPost("{id}/refresh")]
    public async Task<ActionResult<FeedRefreshResultDto>> Refresh(string id)
    {
        var outcome = await _feedUpdateService.RefreshAsync(id);
        if (outcome == null) return NotFound();

        if (!outcome.Success)
        {
            return Problem(
                title: "Feed could not be refreshed",
                detail: outcome.Error,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var feed = await _feedsService.GetAsync(id);
        if (feed == null) return NotFound();

        return Ok(new FeedRefreshResultDto
        {
            Id = feed.Id,
            Url = feed.Url,
            Title = feed.Title,
            EpisodeCount = outcome.EpisodeCount,
            LastUpdated = feed.LastUpdated,
            NextUpdateAt = feed.NextUpdateAt
        });
    }
}
