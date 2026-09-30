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
    private readonly LinkGenerator _linkGenerator;

    public AccountController(ILogger<AccountController> logger, AccountService accountService, LinkGenerator linkGenerator)
    {
        _logger = logger;
        _accountService = accountService;
        _linkGenerator = linkGenerator;
    }
    
    
    [EndpointSummary("Create an account")]
    [EndpointDescription(@"Add a new account to the database and return it.

200 - Returned existing account.
201 - Created new Account.
400 - Account could not be created.
")]
    [Authorize(Policy = Consts.CreateAccountPolicy)]
    [HttpPost("create")]
    public async Task<ActionResult<Account>> CreateAccount(CreateAccountRequest request)
    {
        var existingAccount = await _accountService.FindByIdpAsync(request.IdentityProvider);
        if (existingAccount != null)
            return Ok(existingAccount);

        // create
        var account = new Account()
        {
            Email = request.Email,
            Name = request.Name,
            ProfilePicture = request.ProfilePicture,
            IdentityProviders = [request.IdentityProvider]
        };
        var result = await _accountService.CreateAsync(account);
        if(result != null)
            return Created(_linkGenerator.GetUriByAction(
                HttpContext,
                action: nameof(GetAccountById),
                values: new { id = result.Id }
                ), result);
        
        return BadRequest();
    }
    
    [EndpointSummary("Find Account Details based on Id. ")]
    [EndpointDescription(@"200 - Found and returns the account.

404 - Account not found")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpGet("id/{id}")]
    public async Task<ActionResult<Account>> GetAccountById(string id)
    {
        var existingAccount = await _accountService.GetAsync(id);
        if (existingAccount != null)
            return Ok(existingAccount);
        return NotFound();
    }
    [EndpointSummary("Find Account based on E-Mail. Useful to get Account Id.")]
    [EndpointDescription(@"200 - Found and returns the account.

404 - Account not found")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpGet("email/{email}")]
    public async Task<ActionResult<Account>> GetAccountByEmail(string email)
    {
        var existingAccount = await _accountService.FindByEmailAddress(email);
        if (existingAccount != null)
            return Ok(existingAccount);
        return NotFound();
    }
    
    [EndpointSummary("Add Identity Provider to Account based on Account Id")]
    [EndpointDescription(@"200 - Found Account, added Identity Provider and returns the account.

404 - Account not found

409 - Identity Provider already linked
")]
    [Authorize(Policy = Consts.AddIdpPolicy)]
    [HttpPost("idp")]
    public async Task<ActionResult<Account>> AddIdp(IdentityProviderRequest request)
    {
        var existingAccount = await _accountService.GetAsync(request.AccountId);
        if (existingAccount == null)
            return NotFound();

        var existingIdp = existingAccount.IdentityProviders.FirstOrDefault(x => x.Issuer == request.Issuer);
        if (existingIdp != null)
            return Conflict("Identity Provider already linked");
        
        existingAccount.IdentityProviders.Add(new IdpUser{Issuer = request.Issuer, UserId = request.UserId});
        var result = await _accountService.UpdateAsync(existingAccount);
        return Ok(result!);
    }
    
    [EndpointSummary("Remove Identity Provider From Account based on Account Id")]
    [EndpointDescription(@"200 - Found Account, removed Identity Provider and returns the account.

404 - Account not found

409 - Identity Provider not found
")]
    [Authorize(Policy = Consts.RemoveIdpPolicy)]
    [HttpDelete("idp")]
    public async Task<ActionResult<Account>> RemoveIdp(IdentityProviderRequest request)
    {
        var existingAccount = await _accountService.GetAsync(request.AccountId);
        if (existingAccount == null)
            return NotFound();

        var existingIdp = existingAccount.IdentityProviders.FirstOrDefault(x => x.Issuer == request.Issuer);
        if (existingIdp == null)
            return Conflict("Identity Provider not found");
        
        existingAccount.IdentityProviders.Remove(existingIdp);
        var result = await _accountService.UpdateAsync(existingAccount);
        return Ok(result!);
    }
}