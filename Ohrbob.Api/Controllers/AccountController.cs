using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly ILogger<AccountController> _logger;
    private readonly AccountService _accountService;

    public AccountController(ILogger<AccountController> logger, AccountService accountService)
    {
        _logger = logger;
        _accountService = accountService;
    }
    [Authorize(Policy = "CreateAccount")]
    [HttpPost("create")]
    public async Task<IActionResult> CreateAccount(CreateAccountRequest request)
    {
        var account = new Account()
        {
            Email = request.Email,
            Name = request.Name,
            ProfilePicture = request.ProfilePicture,
            IdentityProviders = [new IdpUser{Issuer = request.IdentityProvider, UserId = request.UserId}]
        };
        var result = await _accountService.CreateAsync(account);
        return Ok(result);
    }
}