using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly ILogger<AccountController> _logger;

    public AccountController(ILogger<AccountController> logger)
    {
        _logger = logger;
    }
    [Authorize(Policy = "CreateAccount")]
    [HttpPost("create")]
    public async Task<IActionResult> CreateAccount(CreateAccountRequest request)
    {
        _logger.LogAudit("Account.CreateAccount", request);
        return Ok();
    }
}