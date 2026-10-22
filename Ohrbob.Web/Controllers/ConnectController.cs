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

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Please enter a name.";
            return View(await BuildCardModel(accountId));
        }

        try
        {
            var apiKey = new ApiKey
            {
                AccountId = accountId,
                Name = name.Trim(),
                ProfileId = profileId ?? string.Empty
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
        return View(await BuildCardModel(accountId));
    }

    private async Task<ConnectAppViewModel> BuildCardModel(string accountId)
    {
        var profiles = await _profiles.GetProfilesByAccountIdAsync(accountId) ?? new();
        return new ConnectAppViewModel { Profiles = profiles };
    }
}
