using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    public AccountController()
    {
        
    }
    [Authorize(Policy = "CreateAccount")]
    [HttpPost("create")]
    public async Task<IActionResult> CreateAccount(CreateAccountRequest request)
    {
        return Ok();
    }
}