using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class ConnectController : Controller
{
    private readonly ProfilesApiService _profiles;
    private readonly ApiKeysApiService _apiKeys;
    private readonly OhrbobApiOptions _apiOptions;

    public ConnectController(ProfilesApiService profiles, ApiKeysApiService apiKeys, IOptions<OhrbobApiOptions> apiOptions)
    {
        _profiles = profiles;
        _apiKeys = apiKeys;
        _apiOptions = apiOptions.Value;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [HttpGet]
    public async Task<IActionResult> App()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        return View(await BuildCardModel(accountId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> App(string name, string profileId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profiles = await _profiles.GetProfilesByAccountIdAsync(accountId) ?? new();
        var submittedProfileId = profileId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Please enter a name.";
            return View(BuildCardModel(profiles, name, submittedProfileId));
        }

        // A key may only be scoped to a profile that belongs to the signed-in account. Blank means
        // account-level scope and is always allowed; anything else must match one of the account's
        // own profiles so a key can never be pointed at another account's profile.
        if (!string.IsNullOrEmpty(submittedProfileId) && !profiles.Any(p => p.Id == submittedProfileId))
        {
            TempData["Error"] = "Please choose a profile from this account.";
            return View(BuildCardModel(profiles, name, submittedProfileId));
        }

        try
        {
            var apiKey = new ApiKey
            {
                AccountId = accountId,
                Name = name.Trim(),
                ProfileId = submittedProfileId
            };
            var createdKey = await _apiKeys.CreateApiKeyAsync(accountId, apiKey);
            if (createdKey != null)
            {
                var deepLink = $"ohrbob://connect?server={Uri.EscapeDataString(_apiOptions.FrontendPlayerUrl)}&token={Uri.EscapeDataString(createdKey.Token)}";
                return View("Handoff", deepLink);
            }
        }
        catch
        {
            // Fall through to the recoverable error card below.
        }

        TempData["Error"] = "Failed to create API key.";
        return View(BuildCardModel(profiles, name, submittedProfileId));
    }

    private async Task<ConnectAppViewModel> BuildCardModel(string accountId)
    {
        var profiles = await _profiles.GetProfilesByAccountIdAsync(accountId) ?? new();
        return BuildCardModel(profiles, string.Empty, string.Empty);
    }

    private static ConnectAppViewModel BuildCardModel(List<Profile> profiles, string? name, string? profileId)
    {
        return new ConnectAppViewModel
        {
            Profiles = profiles,
            Name = name ?? string.Empty,
            ProfileId = profileId ?? string.Empty
        };
    }
}
