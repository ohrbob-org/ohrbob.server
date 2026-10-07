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
    private readonly StationsService _stationsService;
    private readonly AccountService? _accountService;
    private readonly SubscriptionPolicy? _policy;

    public ProfilesController(ProfilesService profilesService, StationsService stationsService, AccountService? accountService = null, SubscriptionPolicy? policy = null)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
        _accountService = accountService;
        _policy = policy;
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
        
        if (_accountService != null && _policy != null)
        {
            var account = await _accountService.GetAsync(accountId);
            if (account == null) return NotFound("Account not found");
            var limits = _policy.For(account);
            var existing = await _profilesService.GetByAccountIdAsync(accountId);
            if(existing.Count > 0)
                profile = existing.First();
        }

        
        
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


    [EndpointSummary("Add a station to a profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("{id}/stations/{stationId}")]
    public async Task<IActionResult> AddStation(string id, string stationId)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound("Profile not found");

        var station = await _stationsService.GetAsync(stationId);
        if (station == null) return NotFound("Station not found");
        
        if (station.AccountId != profile.AccountId) return Forbid();

        var allowedList = profile.AllowedStationIds?.ToList() ?? new List<string>();
        if (!allowedList.Contains(stationId))
        {
            allowedList.Add(stationId);
            profile.AllowedStationIds = allowedList.ToArray();
            await _profilesService.UpdateAsync(profile);
        }

        return Ok();
    }

    [EndpointSummary("Remove a station from a profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpDelete("{id}/stations/{stationId}")]
    public async Task<IActionResult> RemoveStation(string id, string stationId)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound("Profile not found");

        var allowedList = profile.AllowedStationIds?.ToList() ?? new List<string>();
        if (allowedList.Contains(stationId))
        {
            allowedList.Remove(stationId);
            profile.AllowedStationIds = allowedList.ToArray();
            await _profilesService.UpdateAsync(profile);
        }

        return Ok();
    }

}
