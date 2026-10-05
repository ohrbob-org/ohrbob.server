using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class OnboardingController : Controller
{
    private readonly ProfilesApiService _profilesService;
    private readonly StationsApiService _stationsService;
    private readonly AccountApiService _accountService;
    private readonly PodcastIndexApiService _podcastApi;

    public OnboardingController(
        ProfilesApiService profilesService, 
        StationsApiService stationsService, 
        AccountApiService accountService,
        PodcastIndexApiService podcastApi)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
        _accountService = accountService;
        _podcastApi = podcastApi;
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
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProfile(string profileName)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrWhiteSpace(profileName)) return RedirectToAction(nameof(Index));

        var profile = new Profile
        {
            AccountId = accountId,
            Name = profileName,
            StreamToken = Guid.NewGuid().ToString()
        };

        var created = await _profilesService.CreateProfileAsync(accountId, profile);
        
        // Store profile ID in TempData for the next step
        TempData["OnboardingProfileId"] = created?.Id;
        
        return RedirectToAction(nameof(Station));
    }

    [HttpGet]
    public IActionResult Station()
    {
        if (TempData["OnboardingProfileId"] == null)
            return RedirectToAction(nameof(Index));
            
        TempData.Keep("OnboardingProfileId");
        
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
    public IActionResult CreateStation(string stationName)
    {
        if (TempData["OnboardingProfileId"] == null || string.IsNullOrWhiteSpace(stationName))
            return RedirectToAction(nameof(Index));
            
        TempData.Keep("OnboardingProfileId");
        TempData["OnboardingStationName"] = stationName;
        
        return RedirectToAction("Feeds");
    }

    [HttpGet]
    public IActionResult Feeds()
    {
        if (TempData["OnboardingProfileId"] == null || TempData["OnboardingStationName"] == null)
            return RedirectToAction(nameof(Index));
            
        TempData.Keep("OnboardingProfileId");
        TempData.Keep("OnboardingStationName");
        
        ViewBag.StationName = TempData["OnboardingStationName"] as string;
        return View();
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
    public async Task<IActionResult> Finish(List<string> selectedFeeds)
    {
        var accountId = GetAccountId();
        var profileId = TempData["OnboardingProfileId"] as string;
        var stationName = TempData["OnboardingStationName"] as string;

        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(stationName))
            return RedirectToAction(nameof(Index));

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

        return RedirectToAction("Index", "Home");
    }
}