using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Route("[controller]")]
public class AccountController : Controller
{
    private readonly AccountApiService _accountService;

    public AccountController(AccountApiService accountService)
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
