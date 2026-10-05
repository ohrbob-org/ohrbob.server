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
    private readonly ProfilesService _profilesService;
    private readonly LinkGenerator _linkGenerator;

    public AccountController(ILogger<AccountController> logger, AccountService accountService, ProfilesService profilesService, LinkGenerator linkGenerator)
    {
        _logger = logger;
        _accountService = accountService;
        _profilesService = profilesService;
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
            IdentityProviders = [request.IdentityProvider],
            NeedsOnboarding = true
        };
        var result = await _accountService.CreateAsync(account);
        if(result != null)
        {
            return Created(_linkGenerator.GetUriByAction(
                HttpContext,
                action: nameof(GetAccountById),
                values: new { id = result.Id }
                ), result);
        }
        
        return BadRequest();
    }

    [EndpointSummary("Mark onboarding as complete")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("id/{id}/complete-onboarding")]
    public async Task<ActionResult> CompleteOnboarding(string id)
    {
        var account = await _accountService.FindByIdAsync(id);
        if (account == null) return NotFound();

        account.NeedsOnboarding = false;
        await _accountService.UpdateAsync(account);
        return Ok();
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
    [EndpointSummary("Set active profile for account")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("{accountId}/active-profile/{profileId}")]
    public async Task<ActionResult<Account>> SetActiveProfile(string accountId, string profileId)
    {
        // Actually, the AccountId is normally required or inferred.
        // GetAccountPolicy checks API Key or Claims. Let's just use the claim.
        
        
        // Fallback for API Key users in tests if claim isn't present
        if (string.IsNullOrEmpty(accountId) && User.Identity?.IsAuthenticated == true)
        {
            // Try to find account by IDP or whatever logic the auth uses, or just pass accountId in request.
            // Wait, we can't easily get it without passing it. Let's change the route to be explicit if needed.
        }

        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var account = await _accountService.GetAsync(accountId);
        if (account == null) return NotFound("Account not found");

        var profile = await _profilesService.GetAsync(profileId);
        if (profile == null || profile.AccountId != accountId) return NotFound("Profile not found or access denied");

        account.LastUsedProfileId = profileId;
        var updated = await _accountService.UpdateAsync(account);
        return Ok(updated);
    }
}
