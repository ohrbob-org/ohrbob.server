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
}