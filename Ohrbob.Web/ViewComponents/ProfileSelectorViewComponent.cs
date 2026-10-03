using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Services;
using Ohrbob.Server.Shared.Data;
using System.Security.Claims;
using Ohrbob.Web;

namespace Ohrbob.Web.ViewComponents;

public class ProfileSelectorViewComponent : ViewComponent
{
    private readonly ProfilesApiService _profilesService;
    private readonly AccountApiService _accountService;

    public ProfileSelectorViewComponent(ProfilesApiService profilesService, AccountApiService accountService)
    {
        _profilesService = profilesService;
        _accountService = accountService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var accountId = HttpContext.User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;
        if (string.IsNullOrEmpty(accountId)) return Content(string.Empty);

        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId) ?? new List<Profile>();
        if (!profiles.Any())
        {
            return View(new ProfileSelectorViewModel());
        }

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        
        if (string.IsNullOrEmpty(activeId) || !profiles.Any(p => p.Id == activeId))
        {
            var account = await _accountService.GetAccountAsync(accountId);
            activeId = account?.LastUsedProfileId;

            if (string.IsNullOrEmpty(activeId) || !profiles.Any(p => p.Id == activeId))
            {
                activeId = profiles.First().Id;
            }
            
            // Sync cookie
            HttpContext.Response.Cookies.Append("Ohrbob_ActiveProfileId", activeId, new CookieOptions { HttpOnly = true, Secure = true, Expires = DateTimeOffset.UtcNow.AddDays(30) });
        }

        var activeProfile = profiles.First(p => p.Id == activeId);

        var model = new ProfileSelectorViewModel
        {
            ActiveProfile = activeProfile,
            AllProfiles = profiles
        };

        return View(model);
    }
}

public class ProfileSelectorViewModel
{
    public Profile ActiveProfile { get; set; } = new Profile();
    public List<Profile> AllProfiles { get; set; } = new List<Profile>();
}
