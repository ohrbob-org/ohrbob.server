using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;
using System.Security.Claims;

namespace Ohrbob.Web.ViewComponents;

public class PlayerWidgetViewComponent : ViewComponent
{
    private readonly ProfilesApiService _profilesService;
    private readonly AccountApiService _accountService;
    private readonly OhrbobApiOptions _options;

    public PlayerWidgetViewComponent(ProfilesApiService profilesService, AccountApiService accountService, IOptions<OhrbobApiOptions> options)
    {
        _profilesService = profilesService;
        _accountService = accountService;
        _options = options.Value;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new PlayerWidgetViewModel
        {
            StreamToken = string.Empty,
            PlayerApiUrl = _options.PlayerApiUrl
        };

        var accountId = HttpContext.User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;
        if (string.IsNullOrEmpty(accountId)) return View(model);

        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId) ?? new List<Profile>();
        if (!profiles.Any()) return View(model);

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        
        if (string.IsNullOrEmpty(activeId) || !profiles.Any(p => p.Id == activeId))
        {
            var account = await _accountService.GetAccountAsync(accountId);
            activeId = account?.LastUsedProfileId;

            if (string.IsNullOrEmpty(activeId) || !profiles.Any(p => p.Id == activeId))
            {
                activeId = profiles.First().Id;
            }
        }

        var activeProfile = profiles.First(p => p.Id == activeId);
        model.StreamToken = activeProfile.StreamToken;

        return View(model);
    }
}

public class PlayerWidgetViewModel
{
    public string StreamToken { get; set; } = string.Empty;
    public string PlayerApiUrl { get; set; } = string.Empty;
}