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

    public ProfilesController(ProfilesApiService profilesService, StationsApiService stationsService)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
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
            await _profilesService.CreateProfileAsync(accountId, profile);
            return RedirectToAction(nameof(Index));
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
        ViewBag.Stations = stations ?? new List<Station>();

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
        ViewBag.Stations = stations ?? new List<Station>();
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

        await _profilesService.DeleteProfileAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
