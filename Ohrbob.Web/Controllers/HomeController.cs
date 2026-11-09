using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Web.Controllers;

public class HomeController : Controller
{
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;
    private readonly LegalOptions _legalOptions;
    private readonly BackendVersionCache _cache;
    private readonly AppVersionOptions _appVersion;

    public HomeController(StationsApiService stationsService, ProfilesApiService profilesService, IOptions<LegalOptions> legalOptions, BackendVersionCache cache, IOptions<AppVersionOptions> appVersion)
    {
        _stationsService = stationsService;
        _profilesService = profilesService;
        _legalOptions = legalOptions.Value;
        _cache = cache;
        _appVersion = appVersion.Value;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [AllowAnonymous]
    public async Task<IActionResult> Index(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new List<StationWithFeedsDto>());
        }

        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return View(new List<StationWithFeedsDto>());

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        
        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
        if (profiles == null || !profiles.Any()) return View(new List<StationWithFeedsDto>());

        var activeProfile = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles.First();
        ViewBag.ActiveStationId = activeProfile.LastActiveStationId;
        
        var allStations = await _stationsService.GetStationsByAccountIdAsync(accountId, activeProfile.Id) ?? new List<StationWithFeedsDto>();
        
        var allowedStationIds = activeProfile.AllowedStationIds ?? Array.Empty<string>();
        var profileStations = allStations.Where(s => allowedStationIds.Contains(s.Station.Id)).ToList();

        var subscribedStationIds = activeProfile.SubscribedStationIds ?? Array.Empty<string>();
        foreach (var stationId in subscribedStationIds)
        {
            if (profileStations.Any(s => s.Station.Id == stationId)) continue;

            var subscribed = await _stationsService.GetStationDetailsByIdAsync(stationId, activeProfile.Id);
            if (subscribed != null) profileStations.Add(subscribed);
        }

        ViewBag.SubscribedStationIds = subscribedStationIds.ToHashSet();

        return View(profileStations);
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View(_legalOptions);
    }

    [AllowAnonymous]
    public IActionResult Legal()
    {
        if (!_legalOptions.IsConfigured)
        {
            return NotFound();
        }
        return View(_legalOptions);
    }

    [AllowAnonymous]
    public IActionResult About()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Status()
    {
        return View(BuildAboutViewModel());
    }

    [AllowAnonymous]
    public IActionResult Features()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult SelfHost()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Faq()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Clients()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult AboutStatus()
    {
        return PartialView("_AboutStatus", BuildAboutViewModel());
    }

    private AboutViewModel BuildAboutViewModel()
    {
        var v = _appVersion;
        return new AboutViewModel
        {
            WebVersion = v.Version,
            WebTag = v.Tag,
            ApiVersion = _cache.ApiVersion,
            ApiTag = _cache.ApiTag,
            ApiReachable = _cache.ApiReachable,
            PlayerVersion = _cache.PlayerVersion,
            PlayerTag = _cache.PlayerTag,
            PlayerReachable = _cache.PlayerReachable,
            LastCheckedUtc = _cache.LastCheckedUtc
        };
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
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult AlreadyRegistered()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Login(string provider, string? returnUrl = null, string? region = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var targetUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
            return Redirect(targetUrl);
        }

        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        foreach (var p in OidcProviderConfig.All)
        {
            ViewData[p + "Enabled"] = OidcProviderConfig.IsEnabled(config, p);
        }
        ViewBag.ReturnUrl = returnUrl;
        if (string.IsNullOrEmpty(provider) || !OidcProviderConfig.IsEnabled(config, provider))
        {
            return View();
        }
        var redirectUri = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        var properties = new AuthenticationProperties { RedirectUri = redirectUri, IsPersistent = true };
        if (provider == "Amazon")
        {
            var domain = OidcProviderConfig.AmazonDomain(region);
            if (domain is null)
            {
                return View();
            }
            properties.Items["amazon_domain"] = domain;
        }
        return Challenge(properties, provider);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Index));
    }
}