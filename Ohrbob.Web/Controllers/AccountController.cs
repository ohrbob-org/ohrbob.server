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
    private readonly ApiKeysApiService _apiKeys;

    public AccountController(AccountApiService accounts, StationsApiService stations, ProfilesApiService profiles, ApiKeysApiService apiKeys)
    {
        _accounts = accounts;
        _stations = stations;
        _profiles = profiles;
        _apiKeys = apiKeys;
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
        var apiKeys = await _apiKeys.GetApiKeysByAccountIdAsync(accountId) ?? new();
        var model = new ManageAccountViewModel
        {
            Account = account,
            Limits = limits,
            StationCount = stations.Count,
            ProfileCount = profiles.Count,
            MaxFeedsPerStationUsed = stations.Select(s => s.Station.Feeds?.Length ?? 0).DefaultIfEmpty(0).Max(),
            ApiKeys = apiKeys,
            Profiles = profiles
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddApiKey(string name, string profileId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var apiKey = new ApiKey
            {
                AccountId = accountId,
                Name = name,
                ProfileId = profileId ?? string.Empty
            };
            var createdKey = await _apiKeys.CreateApiKeyAsync(accountId, apiKey);
            if (createdKey != null)
            {
                TempData["Notice"] = "API key created successfully.";
                TempData["NewApiKeyName"] = createdKey.Name;
                TempData["NewApiKeyToken"] = createdKey.Token;
            }
        }
        catch
        {
            TempData["Error"] = "Failed to create API key.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeApiKey(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        try
        {
            await _apiKeys.DeleteApiKeyAsync(id);
            TempData["Notice"] = "API key revoked.";
        }
        catch
        {
            TempData["Error"] = "Failed to revoke API key.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upgrade()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();
        try
        {
            await _accounts.SetTierAsync(accountId, SubscriptionTier.Morebob);
            TempData["Notice"] = "Upgraded to Morebob (beta — billing integration coming soon).";
        }
        catch
        {
            TempData["Error"] = "Upgrade failed. Please try again.";
        }
        return RedirectToAction(nameof(Index));
    }
}
