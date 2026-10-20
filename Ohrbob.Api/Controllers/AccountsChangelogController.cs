using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/account")]
public class AccountsChangelogController : ControllerBase
{
    private readonly AccountService _accountService;

    public AccountsChangelogController(AccountService accountService)
    {
        _accountService = accountService;
    }

    [EndpointSummary("Update last seen changelog")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("id/{id}/changelog")]
    public async Task<ActionResult<Account>> UpdateChangelog(string id, [FromBody] UpdateLastSeenChangelogRequest request)
    {
        var account = await _accountService.GetAsync(id);
        if (account == null) return NotFound();

        account.LastSeenChangelog = request.LastSeenChangelog;
        var updated = await _accountService.UpdateAsync(account);
        return Ok(updated);
    }
}
