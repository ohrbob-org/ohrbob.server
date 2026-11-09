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
            AllowedStationIds = request.AllowedStationIds ?? Array.Empty<string>(),
            Color = request.Color
        };
        
        if (!ProfileColors.IsValid(profile.Color)) return BadRequest("Invalid color");
        
        if (_accountService != null && _policy != null)
        {
            var account = await _accountService.GetAsync(accountId);
            if (account == null) return NotFound("Account not found");
            var limits = _policy.For(account);
            var existing = await _profilesService.GetByAccountIdAsync(accountId);
            if(existing.Count >= limits.MaxProfiles)
                return BadRequest("Profile limit reached");
        }
        var created = await _profilesService.CreateAsync(profile);
        return Ok(created);
    }

    [EndpointSummary("Update profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPut()]
    public async Task<IActionResult> Update([FromBody] Profile profile)
    {
        if (!ProfileColors.IsValid(profile.Color)) return BadRequest("Invalid color");

        // The profile edit form does not round-trip subscriptions, so an empty list means
        // "unchanged" rather than "remove every subscription".
        if (profile.SubscribedStationIds == null || profile.SubscribedStationIds.Length == 0)
        {
            var existing = await _profilesService.GetAsync(profile.Id);
            if (existing == null) return NotFound();
            profile.SubscribedStationIds = existing.SubscribedStationIds;
        }

        var updated = await _profilesService.UpdateAsync(profile);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [EndpointSummary("Subscribe a profile to a public station")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("{id}/subscriptions/{stationId}")]
    public async Task<IActionResult> Subscribe(string id, string stationId)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound("Profile not found");

        var station = await _stationsService.GetAsync(stationId);
        if (station == null) return NotFound("Station not found");

        if (!station.IsPublic || station.AccountId == profile.AccountId)
            return Forbid();

        var subscribed = profile.SubscribedStationIds?.ToList() ?? new List<string>();
        if (!subscribed.Contains(stationId))
        {
            subscribed.Add(stationId);
            profile.SubscribedStationIds = subscribed.ToArray();
            await _profilesService.UpdateAsync(profile);
        }

        return Ok();
    }

    [EndpointSummary("Unsubscribe a profile from a station")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpDelete("{id}/subscriptions/{stationId}")]
    public async Task<IActionResult> Unsubscribe(string id, string stationId)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound("Profile not found");

        var subscribed = profile.SubscribedStationIds?.ToList() ?? new List<string>();
        if (subscribed.Remove(stationId))
        {
            profile.SubscribedStationIds = subscribed.ToArray();
            await _profilesService.UpdateAsync(profile);
        }

        return Ok();
    }

    [EndpointSummary("Delete profile")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var profile = await _profilesService.GetAsync(id);
        if (profile == null) return NotFound();

        // Enforce Profiles Minimum Requirement: Every account must have at least one profile.
        var profiles = await _profilesService.GetByAccountIdAsync(profile.AccountId);
        if (profiles == null || profiles.Count <= 1)
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Profile Delete Error",
                Detail = "Cannot delete the last remaining profile of an account."
            });
        }

        var result = await _profilesService.DeleteAsync(id);
        if (!result) return NotFound();

        // A station cannot be host-paced by a deleted profile.
        var hostedStations = await _stationsService.GetByHostProfileIdAsync(id);
        foreach (var hostedStation in hostedStations)
        {
            hostedStation.HostPaceEnabled = false;
            hostedStation.HostProfileId = null;
            await _stationsService.UpdateAsync(hostedStation);
        }

        if (_accountService != null)
        {
            var account = await _accountService.GetAsync(profile.AccountId);
            if (account != null && account.LastUsedProfileId == id)
            {
                account.LastUsedProfileId = string.Empty;
                await _accountService.UpdateAsync(account);
            }
        }

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
