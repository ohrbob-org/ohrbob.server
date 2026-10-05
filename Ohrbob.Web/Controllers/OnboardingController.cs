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
}