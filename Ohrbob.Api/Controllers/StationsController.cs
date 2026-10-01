using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private readonly StationsService _stationsService;
    private readonly FeedsService _feedsService;
    private readonly EpisodesService _episodesService;

    public StationsController(StationsService stationsService, FeedsService feedsService, EpisodesService episodesService)
    {
        _stationsService = stationsService;
        _feedsService = feedsService;
        _episodesService = episodesService;
    }
    [EndpointSummary("Get all stations by accountId")]
    [EndpointDescription(@"Returns a list of all stations associated with an accountId")]
    [Authorize(policy: Consts.GetStationsPolicy)]
    [HttpGet("account/{accountId}")]
    public async Task<ActionResult<List<Station>>> GetByAccountId(string accountId)
    {
        var stations = await _stationsService.GetByAccountIdAsync(accountId);
        return Ok(stations);
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
                    stats.EpisodeCount = await _episodesService.CountByFeedIdAsync(feed.Id);
                }
                dto.Feeds.Add(stats);
            }
        }
        
        return Ok(dto);
    }

    [EndpointSummary("Create station for accountId")]
    [EndpointDescription(@"Creates station and returns it.")]
    [Authorize(policy: Consts.CreateStationPolicy)]
    [HttpPost("{accountId}")]
    public async Task<IActionResult> Create(string accountId, [FromBody] Station request)
    {
        var station = new Station
        {
            AccountId = accountId,
            Name = request.Name,
            Feeds = request.Feeds
        };
        var createdStation = await _stationsService.CreateAsync(station);
        return Ok(createdStation);
    }

    [EndpointSummary("Update station")]
    [Authorize(policy:Consts.UpdateStationPolicy)]
    [HttpPut()]
    public async Task<IActionResult> Update([FromBody] Station station)
    {

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
        var result = await _stationsService.DeleteAsync(id);
        if (!result)
        {
            return NotFound();
        }
        return Ok();
    }
}
