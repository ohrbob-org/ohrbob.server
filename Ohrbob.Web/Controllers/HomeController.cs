using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Web.Controllers;

public class HomeController : Controller
{
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;

    public HomeController(StationsApiService stationsService, ProfilesApiService profilesService)
    {
        _stationsService = stationsService;
        _profilesService = profilesService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View(new List<StationWithFeedsDto>());
        }

        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return View(new List<StationWithFeedsDto>());

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        
        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
        if (profiles == null || !profiles.Any()) return View(new List<StationWithFeedsDto>());

        var activeProfile = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles.First();
        ViewBag.ActiveStationId = activeProfile.LastActiveStationId;
        
        var allStations = await _stationsService.GetStationsByAccountIdAsync(accountId) ?? new List<StationWithFeedsDto>();
        
        var allowedStationIds = activeProfile.AllowedStationIds ?? Array.Empty<string>();
        var profileStations = allStations.Where(s => allowedStationIds.Contains(s.Station.Id)).ToList();

        return View(profileStations);
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult ServiceUnavailable()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Login(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            return View();
        }
        return Challenge(new AuthenticationProperties { RedirectUri = "/" }, provider);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Index));
    }
}