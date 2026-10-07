using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly AccountApiService _accounts;
    private readonly StationsApiService _stations;
    private readonly ProfilesApiService _profiles;

    public AccountController(AccountApiService accounts, StationsApiService stations, ProfilesApiService profiles)
    {
        _accounts = accounts;
        _stations = stations;
        _profiles = profiles;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();
        var account = await _accounts.GetAccountAsync(accountId);
        if (account == null) return NotFound();
        var limits = await _accounts.GetLimitsAsync(accountId) ?? new TierLimits();
        var stations = await _stations.GetStationsByAccountIdAsync(accountId) ?? new();
        var profiles = await _profiles.GetProfilesByAccountIdAsync(accountId) ?? new();
        var model = new ManageAccountViewModel
        {
            Account = account,
            Limits = limits,
            StationCount = stations.Count,
            ProfileCount = profiles.Count,
            MaxFeedsPerStationUsed = stations.Select(s => s.Station.Feeds?.Length ?? 0).DefaultIfEmpty(0).Max()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upgrade()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();
        try
        {
            await _accounts.SetTierAsync(accountId, SubscriptionTier.Premium);
            TempData["Notice"] = "Upgraded to Premium (beta — billing integration coming soon).";
        }
        catch
        {
            TempData["Error"] = "Upgrade failed. Please try again.";
        }
        return RedirectToAction(nameof(Index));
    }
}
