using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class ProfilesController : Controller
{
    private readonly ProfilesApiService _profilesService;
    private readonly StationsApiService _stationsService;
    private readonly AccountApiService _accountService;

    public ProfilesController(ProfilesApiService profilesService, StationsApiService stationsService, AccountApiService accountService)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
        _accountService = accountService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
        return View(profiles ?? new List<Profile>());
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Profile());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Profile profile)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (ModelState.IsValid)
        {
            try
            {
                await _profilesService.CreateProfileAsync(accountId, profile);
                return RedirectToAction(nameof(Index));
            }
            catch (LimitExceededException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }
        return View(profile);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profile = await _profilesService.GetProfileByIdAsync(id);
        if (profile == null || profile.AccountId != accountId)
            return NotFound();

        var stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        ViewBag.Stations = stations ?? new List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>();

        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Profile profile, List<string> selectedStations)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (id != profile.Id)
            return BadRequest();

        var existingProfile = await _profilesService.GetProfileByIdAsync(id);
        if (existingProfile == null || existingProfile.AccountId != accountId)
            return NotFound();

        if (ModelState.IsValid)
        {
            // Update profile info
            profile.AccountId = accountId;
            profile.StreamToken = existingProfile.StreamToken; // ensure it isn't wiped out
            profile.SubscribedStationIds = existingProfile.SubscribedStationIds ?? Array.Empty<string>();
            await _profilesService.UpdateProfileAsync(profile);

            // Handle stations assignments
            var currentStationIds = existingProfile.AllowedStationIds?.ToList() ?? new List<string>();
            var newStationIds = selectedStations ?? new List<string>();

            // Find which to add
            var toAdd = newStationIds.Except(currentStationIds).ToList();
            foreach (var sId in toAdd)
            {
                await _profilesService.AddStationToProfileAsync(id, sId);
            }

            // Find which to remove
            var toRemove = currentStationIds.Except(newStationIds).ToList();
            foreach (var sId in toRemove)
            {
                await _profilesService.RemoveStationFromProfileAsync(id, sId);
            }

            return RedirectToAction(nameof(Index));
        }

        var stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        ViewBag.Stations = stations ?? new List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>();
        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profile = await _profilesService.GetProfileByIdAsync(id);
        if (profile == null || profile.AccountId != accountId)
            return NotFound();

        // Enforce Profiles Minimum Requirement: Every account must have at least one profile.
        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
        if (profiles == null || profiles.Count <= 1)
        {
            TempData["Error"] = "Cannot delete the last remaining profile of an account.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _profilesService.DeleteProfileAsync(id);
        }
        catch (LimitExceededException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string id, string? returnUrl)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profile = await _profilesService.GetProfileByIdAsync(id);
        if (profile != null && profile.AccountId == accountId)
        {
            await _accountService.SetActiveProfileAsync(accountId, id);
            Response.Cookies.Append("Ohrbob_ActiveProfileId", id, new CookieOptions { 
                HttpOnly = true, 
                Secure = true, 
                Expires = DateTimeOffset.UtcNow.AddDays(30) 
            });
        }

        var targetUrl = (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) ? returnUrl : Url.Action(nameof(Index), "Home");

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            Response.Headers["HX-Redirect"] = targetUrl;
            return Ok();
        }

        return Redirect(targetUrl!);
    }
}
