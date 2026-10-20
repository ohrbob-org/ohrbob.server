using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Ohrbob.Web.Controllers.Components;

public class ChangelogPopupViewComponent : ViewComponent
{
    private readonly AccountApiService _accountService;
    private readonly AppVersionOptions _appVersion;
    private readonly ChangelogService _changelogService;

    public ChangelogPopupViewComponent(AccountApiService accountService, IOptions<AppVersionOptions> appVersion, ChangelogService changelogService)
    {
        _accountService = accountService;
        _appVersion = appVersion.Value;
        _changelogService = changelogService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View(new ChangelogViewModel { ShowChangelog = false });
        }

        var accountId = HttpContext.User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;
        if (string.IsNullOrEmpty(accountId))
        {
            return View(new ChangelogViewModel { ShowChangelog = false });
        }

        var account = await _accountService.GetAccountAsync(accountId);
        if (account == null)
        {
            return View(new ChangelogViewModel { ShowChangelog = false });
        }

        var currentVersion = _appVersion.Tag ?? _appVersion.Version;
        if (string.IsNullOrEmpty(currentVersion) || account.LastSeenChangelog == currentVersion)
        {
            return View(new ChangelogViewModel { ShowChangelog = false });
        }

        var html = await _changelogService.GetChangelogHtmlAsync();
        if (string.IsNullOrEmpty(html))
        {
             return View(new ChangelogViewModel { ShowChangelog = false });
        }

        return View(new ChangelogViewModel 
        { 
            ShowChangelog = true, 
            Version = currentVersion,
            ChangelogHtml = html
        });
    }
}
