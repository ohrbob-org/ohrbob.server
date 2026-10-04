using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class DiscoverController : Controller
{
    private readonly PodcastIndexApiService _podcastIndexApi;
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;

    public DiscoverController(PodcastIndexApiService podcastIndexApi, StationsApiService stationsService, ProfilesApiService profilesService)
    {
        _podcastIndexApi = podcastIndexApi;
        _stationsService = stationsService;
        _profilesService = profilesService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public IActionResult Index()
    {
        return View();
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
    public async Task<IActionResult> AddFeed(string feedUrl, string feedTitle, string? stationId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Unauthorized();

        if (string.IsNullOrEmpty(feedUrl) || string.IsNullOrEmpty(feedTitle))
        {
            return Content("<div class='alert alert-danger p-2 mb-0'>Invalid feed data.</div>");
        }

        try
        {
            if (string.IsNullOrEmpty(stationId) || stationId == "new")
            {
                var newStation = new Station
                {
                    AccountId = accountId,
                    Name = feedTitle,
                    Feeds = new[] { feedUrl }
                };
                var created = await _stationsService.CreateStationAsync(accountId, newStation);
                
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

                Response.Headers["HX-Trigger"] = "stationAdded";
                return Content($"<div class='alert alert-success p-2 mb-0'>Created new station: {feedTitle}</div>");
            }
            else
            {
                var station = await _stationsService.GetStationByIdAsync(stationId);
                if (station == null || station.AccountId != accountId) return Unauthorized();

                var feeds = station.Feeds?.ToList() ?? new List<string>();
                if (!feeds.Contains(feedUrl))
                {
                    feeds.Add(feedUrl);
                    station.Feeds = feeds.ToArray();
                    await _stationsService.UpdateStationAsync(station);
                }
                
                Response.Headers["HX-Trigger"] = "stationAdded";
                return Content($"<div class='alert alert-success p-2 mb-0'>Added to station: {station.Name}</div>");
            }
        }
        catch (Exception ex)
        {
            return Content($"<div class='alert alert-danger p-2 mb-0'>Error adding feed.</div>");
        }
    }
}