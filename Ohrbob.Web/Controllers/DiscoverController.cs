using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Web.Controllers;

public class AddFeedRequest
{
    public string FeedUrl { get; set; } = string.Empty;
    public string FeedTitle { get; set; } = string.Empty;
    public List<string> StationIds { get; set; } = new();
    public bool CreateNewStation { get; set; }
    public string? NewStationName { get; set; }
}

[Authorize]
public class DiscoverController : Controller
{
    private readonly PodcastIndexApiService _podcastIndexApi;
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;
    private readonly RecommendationService _recommendationService;

    public DiscoverController(PodcastIndexApiService podcastIndexApi, StationsApiService stationsService, ProfilesApiService profilesService, RecommendationService recommendationService)
    {
        _podcastIndexApi = podcastIndexApi;
        _stationsService = stationsService;
        _profilesService = profilesService;
        _recommendationService = recommendationService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public async Task<IActionResult> Index(string? q)
    {
        var accountId = GetAccountId();
        if (accountId != null)
        {
            ViewBag.Stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        }

        ViewBag.Query = q;
        if (!string.IsNullOrWhiteSpace(q))
        {
            ViewBag.SearchResults = await _podcastIndexApi.SearchAsync(q);
        }

        ViewBag.CategoryRoster = await _recommendationService.GetCategoryRosterAsync();
        return View(await _recommendationService.BuildLanesAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Content(""); 
        }

        var results = await _podcastIndexApi.SearchAsync(query);
        
        var accountId = GetAccountId();
        if (accountId != null)
        {
            ViewBag.Stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        }

        return PartialView("_SearchResults", results);
    }

    [HttpPost]
    public async Task<IActionResult> AddFeed([FromBody] AddFeedRequest request)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Unauthorized();

        if (string.IsNullOrEmpty(request.FeedUrl) || string.IsNullOrEmpty(request.FeedTitle))
        {
            return StatusCode(422, "Invalid feed data.");
        }

        if (!request.CreateNewStation && (request.StationIds == null || request.StationIds.Count == 0))
        {
            return StatusCode(422, "No stations selected.");
        }

        try
        {
            var createdStationNames = new List<string>();
            var updatedStationNames = new List<string>();

            // 1. Create New Station if checked
            if (request.CreateNewStation)
            {
                var targetName = !string.IsNullOrWhiteSpace(request.NewStationName) 
                    ? request.NewStationName.Trim() 
                    : request.FeedTitle;

                var newStation = new Station
                {
                    AccountId = accountId,
                    Name = targetName,
                    Feeds = new[] { request.FeedUrl }
                };
                var created = await _stationsService.CreateStationAsync(accountId, newStation);
                createdStationNames.Add(targetName);

                // Add to active profile
                var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
                if (string.IsNullOrEmpty(activeId))
                {
                    var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
                    activeId = profiles?.FirstOrDefault()?.Id;
                }
                if (!string.IsNullOrEmpty(activeId) && created != null)
                {
                    await _profilesService.AddStationToProfileAsync(activeId, created.Id);
                }
            }

            // 2. Add to existing stations
            if (request.StationIds != null && request.StationIds.Count > 0)
            {
                foreach (var stationId in request.StationIds)
                {
                    var station = await _stationsService.GetStationByIdAsync(stationId);
                    if (station == null || station.AccountId != accountId) continue;

                    var feeds = station.Feeds?.ToList() ?? new List<string>();
                    feeds.Add(request.FeedUrl);
                    station.Feeds = feeds.ToArray();

                    if (station.Schedule == null) station.Schedule = new StationSchedule();
                    if (station.Schedule.Feeds == null) station.Schedule.Feeds = new List<StationFeedSchedule>();
                    station.Schedule.Feeds.Add(new StationFeedSchedule { Url = request.FeedUrl });

                    await _stationsService.UpdateStationAsync(station);
                    updatedStationNames.Add(station.Name);
                }
            }

            // 3. Formulate success toast response
            string msg;
            if (createdStationNames.Count > 0 && updatedStationNames.Count > 0)
            {
                msg = $"Created station '{createdStationNames[0]}' and added to: {string.Join(", ", updatedStationNames)}";
            }
            else if (createdStationNames.Count > 0)
            {
                msg = $"Created new station: {createdStationNames[0]}";
            }
            else
            {
                msg = $"Added podcast to: {string.Join(", ", updatedStationNames)}";
            }

            var payload = new { 
                stationAdded = "", 
                showToast = new { message = msg, type = "success" } 
            };
            Response.Headers["HX-Trigger"] = System.Text.Json.JsonSerializer.Serialize(payload);
            return StatusCode(204);
        }
        catch (LimitExceededException ex)
        {
            var payload = new { showToast = new { message = ex.Message, type = "danger" } };
            Response.Headers["HX-Trigger"] = System.Text.Json.JsonSerializer.Serialize(payload);
            return StatusCode(422, ex.Message);
        }
        catch (Exception)
        {
            var payload = new { showToast = new { message = "Error adding feed.", type = "danger" } };
            Response.Headers["HX-Trigger"] = System.Text.Json.JsonSerializer.Serialize(payload);
            return StatusCode(422, "Error adding feed.");
        }
    }
}