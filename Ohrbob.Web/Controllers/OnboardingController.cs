using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class OnboardingController : Controller
{
    private readonly ProfilesApiService _profilesService;
    private readonly StationsApiService _stationsService;
    private readonly AccountApiService _accountService;
    private readonly PodcastIndexApiService _podcastApi;
    private readonly LegalOptions _legalOptions;

    public OnboardingController(
        ProfilesApiService profilesService, 
        StationsApiService stationsService, 
        AccountApiService accountService,
        PodcastIndexApiService podcastApi,
        IOptions<LegalOptions> legalOptions)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
        _accountService = accountService;
        _podcastApi = podcastApi;
        _legalOptions = legalOptions.Value;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var account = await _accountService.GetAccountAsync(accountId);
        if (account == null || !account.NeedsOnboarding)
        {
            return RedirectToAction("Index", "Home");
        }

        // Default profile name based on OIDC claim
        ViewBag.DefaultName = User.Identity?.Name ?? "My Profile";
        ViewBag.LegalShown = _legalOptions.IsConfigured;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProfile(string profileName, bool acceptTerms = false)
    {
        if (_legalOptions.IsConfigured && !acceptTerms)
        {
            TempData["TermsRequired"] = true;
            return RedirectToAction(nameof(Index));
        }
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrWhiteSpace(profileName)) return RedirectToAction(nameof(Index));

        var profile = new Profile
        {
            AccountId = accountId,
            Name = profileName,
            StreamToken = Guid.NewGuid().ToString()
        };

        var created = await _profilesService.CreateProfileAsync(accountId, profile);
        if (string.IsNullOrEmpty(created?.Id)) {
            throw new Exception("CreateProfileAsync returned a null or empty Id! " + System.Text.Json.JsonSerializer.Serialize(created));
        }
        
        return RedirectToAction(nameof(Station), new { profileId = created.Id });
    }

    [HttpGet]
    public IActionResult Station(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return RedirectToAction(nameof(Index));
            
        ViewBag.ProfileId = profileId;
        
        var suggestions = new[] { 
            "Morning Commute", "News", "True Crime", "24h Chill", "Kids Party", 
            "Wake-Up", "Tech News", "Comedy", "History", "Science", 
            "Focus", "Workout", "Bedtime", "Storytelling", "Interviews", 
            "Politics", "Economics", "Pop Culture", "Gaming", "Sports", 
            "Philosophy", "Language Learning", "Food & Drink", "Arts" 
        };
        
        ViewBag.Suggestions = suggestions;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateStation(string profileId, string stationName)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrWhiteSpace(stationName))
            return RedirectToAction(nameof(Index));
            
        return RedirectToAction("Feeds", new { profileId, stationName });
    }

    [HttpGet]
    public async Task<IActionResult> Feeds(string profileId, string stationName)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(stationName))
            return RedirectToAction(nameof(Index));
            
        ViewBag.ProfileId = profileId;
        ViewBag.StationName = stationName;
        
        // Auto-populate 4 podcasts based on station name
        var results = await _podcastApi.SearchAsync(stationName ?? "Podcast");
        var topResults = results?.Take(4).ToList() ?? new List<Ohrbob.Web.Models.PodcastSearchResult>();
        
        return View(topResults);
    }

    [HttpGet]
    public async Task<IActionResult> SearchFeeds(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Content(""); 
        var results = await _podcastApi.SearchAsync(query);
        return PartialView("_FeedSearchResults", results);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(List<string> selectedFeeds, string profileId, string stationName)
    {
        var accountId = GetAccountId();

        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(stationName))
            throw new Exception($"Finish failed. accountId={accountId}, profileId={profileId}, stationName={stationName}");


        // Create Station
        var newStation = new Station
        {
            AccountId = accountId,
            Name = stationName,
            Feeds = selectedFeeds?.ToArray() ?? Array.Empty<string>()
        };
        var createdStation = await _stationsService.CreateStationAsync(accountId, newStation);

        if (createdStation != null)
        {
            // Link station to profile
            await _profilesService.AddStationToProfileAsync(profileId, createdStation.Id);
            
            // Set active profile via cookie so they land straight into it
            Response.Cookies.Append("Ohrbob_ActiveProfileId", profileId, new CookieOptions { 
                HttpOnly = true, Secure = true, Expires = DateTimeOffset.UtcNow.AddDays(30) 
            });
            await _accountService.SetActiveProfileAsync(accountId, profileId);
        }

        // Complete onboarding
        await _accountService.CompleteOnboardingAsync(accountId);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            Response.Headers["HX-Redirect"] = "/";
            return Ok();
        }

        return RedirectToAction("Index", "Home");
    }
}