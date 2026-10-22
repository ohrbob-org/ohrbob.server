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
    private readonly OhrbobApiOptions _apiOptions;

    public ConnectController(ProfilesApiService profiles, IOptions<OhrbobApiOptions> apiOptions)
    {
        _profiles = profiles;
        _apiOptions = apiOptions.Value;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    [HttpGet]
    public async Task<IActionResult> App()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profiles = await _profiles.GetProfilesByAccountIdAsync(accountId) ?? new();
        var model = new ConnectAppViewModel { Profiles = profiles };
        return View(model);
    }
}
