using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Route("s")]
public class ShareController : Controller
{
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;
    private readonly AccountApiService _accountService;

    public ShareController(
        StationsApiService stationsService,
        ProfilesApiService profilesService,
        AccountApiService accountService)
    {
        _stationsService = stationsService;
        _profilesService = profilesService;
        _accountService = accountService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [AllowAnonymous]
    [HttpGet("{code}")]
    public async Task<IActionResult> Index(string code)
    {
        var station = await _stationsService.GetStationByShareCodeAsync(code);
        if (station == null) return NotFound();

        var accountId = GetAccountId();
        var isAuthenticated = !string.IsNullOrEmpty(accountId);
        var isOwner = isAuthenticated && station.Station.AccountId == accountId;

        var model = new ShareViewModel
        {
            Station = station,
            Code = code,
            IsAuthenticated = isAuthenticated,
            IsOwner = isOwner
        };

        if (isAuthenticated && !isOwner)
        {
            var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId!) ?? new List<Profile>();
            var activeId = Request.Cookies["Ohrbob_ActiveProfileId"];
            if (string.IsNullOrEmpty(activeId) || profiles.All(p => p.Id != activeId))
                activeId = profiles.FirstOrDefault()?.Id;

            model.Profiles = profiles.Select(profile => new ShareProfileOption
            {
                Id = profile.Id,
                Name = profile.Name,
                IsSubscribed = (profile.SubscribedStationIds ?? Array.Empty<string>()).Contains(station.Station.Id),
                IsActive = profile.Id == activeId
            }).ToList();
            model.AllProfilesSubscribed = model.Profiles.Count > 0 && model.Profiles.All(p => p.IsSubscribed);
        }

        return View(model);
    }

    [Authorize]
    [HttpPost("{code}/subscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(string code, string profileId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var station = await _stationsService.GetStationByShareCodeAsync(code);
        if (station == null) return NotFound();

        if (station.Station.AccountId == accountId) return Forbid();

        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId) ?? new List<Profile>();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return Forbid();

        await _profilesService.SubscribeToStationAsync(profile.Id, station.Station.Id);
        await _accountService.SetActiveProfileAsync(accountId, profile.Id);

        Response.Cookies.Append("Ohrbob_ActiveProfileId", profile.Id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return RedirectToAction("Index", "Home");
    }
}
