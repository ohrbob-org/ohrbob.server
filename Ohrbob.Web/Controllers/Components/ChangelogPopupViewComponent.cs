using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Markdig;

namespace Ohrbob.Web.Controllers.Components;

public class ChangelogPopupViewComponent : ViewComponent
{
    private readonly AccountApiService _accountService;
    private readonly AppVersionOptions _appVersion;
    private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;

    public ChangelogPopupViewComponent(AccountApiService accountService, IOptions<AppVersionOptions> appVersion, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
    {
        _accountService = accountService;
        _appVersion = appVersion.Value;
        _env = env;
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

        var changelogPath = Path.Combine(_env.ContentRootPath, "changelog.md");
        if (!System.IO.File.Exists(changelogPath))
        {
             return View(new ChangelogViewModel { ShowChangelog = false });
        }

        var markdown = await System.IO.File.ReadAllTextAsync(changelogPath);
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var html = Markdown.ToHtml(markdown, pipeline);

        return View(new ChangelogViewModel 
        { 
            ShowChangelog = true, 
            Version = currentVersion,
            ChangelogHtml = html
        });
    }
}
