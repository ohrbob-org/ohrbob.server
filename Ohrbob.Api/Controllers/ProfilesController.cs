using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfilesController : ControllerBase
{
    private readonly ProfilesService _profilesService;

    public ProfilesController(ProfilesService profilesService)
    {
        _profilesService = profilesService;
    }

    [EndpointSummary("Get all profiles by accountId")]
    [Authorize(Policy = Consts.GetAccountPolicy)] // Reusing GetAccountPolicy for now, ideally separate
    [HttpGet("account/{accountId}")]
    public async Task<ActionResult<List<Profile>>> GetByAccountId(string accountId)
    {
        var profiles = await _profilesService.GetByAccountIdAsync(accountId);
        return Ok(profiles);
    }

    [EndpointSummary("Get profile by id")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpGet("{id}")]
    public async Task<ActionResult<Profile>> GetById(string id)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [EndpointSummary("Create profile for accountId")]
    [Authorize(Policy = Consts.CreateAccountPolicy)]
    [HttpPost("{accountId}")]
    public async Task<ActionResult<Profile>> Create(string accountId, [FromBody] Profile request)
    {
        var profile = new Profile
        {
            AccountId = accountId,
            Name = request.Name,
            StreamToken = Guid.NewGuid().ToString(),
            AllowedStationIds = request.AllowedStationIds ?? Array.Empty<string>()
        };
        
        var created = await _profilesService.CreateAsync(profile);
        return Ok(created);
    }

    [EndpointSummary("Update profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPut()]
    public async Task<IActionResult> Update([FromBody] Profile profile)
    {
        var updated = await _profilesService.UpdateAsync(profile);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [EndpointSummary("Delete profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _profilesService.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }
}
