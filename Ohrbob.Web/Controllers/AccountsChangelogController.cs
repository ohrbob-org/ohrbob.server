using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Services;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Controllers;

[Route("Account")]
public class AccountsChangelogController : Controller
{
    private readonly AccountApiService _accountService;

    public AccountsChangelogController(AccountApiService accountService)
    {
        _accountService = accountService;
    }

    [HttpPost("UpdateLastSeenChangelog")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLastSeenChangelog([FromBody] string version)
    {
        var accountId = User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(version))
        {
            return BadRequest();
        }

        await _accountService.UpdateLastSeenChangelogAsync(accountId, version);
        return Ok();
    }
}
