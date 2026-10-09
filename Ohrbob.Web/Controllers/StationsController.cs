using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class StationsController : Controller
{
    private readonly StationsApiService _stationsService;
    private readonly ProfilesApiService _profilesService;

    public StationsController(StationsApiService stationsService, ProfilesApiService profilesService)
    {
        _stationsService = stationsService;
        _profilesService = profilesService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        return View(stations ?? new List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>());
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Station station)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        // Ensure Feeds array is fully populated from form
        if (Request.Form.TryGetValue("Feeds", out var formFeeds))
        {
            station.Feeds = formFeeds.ToArray();
        }

        try
        {
            var created = await _stationsService.CreateStationAsync(accountId, station);

            // Add to active profile automatically
            var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
            if (string.IsNullOrEmpty(activeId))
            {
                var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
                activeId = profiles?.FirstOrDefault()?.Id;
            }
            if (!string.IsNullOrEmpty(activeId) && created != null)
            {
                await _profilesService.AddStationToProfileAsync(activeId, created.Id);
            }

            return RedirectToAction(nameof(Index));
        }
        catch (LimitExceededException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(station);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var stationDto = await _stationsService.GetStationDetailsByIdAsync(id);
        if (stationDto == null || stationDto.Station.AccountId != accountId)
            return NotFound();

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        if (string.IsNullOrEmpty(activeId))
        {
            var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
            activeId = profiles?.FirstOrDefault()?.Id;
        }

        if (!string.IsNullOrEmpty(activeId))
        {
            ViewBag.Playlist = await _stationsService.GetStationPlaylistAsync(id, activeId);
        }

        return View(stationDto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, [Bind(Prefix = "Station")] Station station)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (id != station.Id)
            return BadRequest();

        // Ensure Feeds array is fully populated from form
        if (Request.Form.TryGetValue("Station.Feeds", out var formFeeds))
        {
            station.Feeds = formFeeds.ToArray();
        }

        var existingStation = await _stationsService.GetStationByIdAsync(id);
        if (existingStation == null || existingStation.AccountId != accountId)
            return NotFound();

        // Ensure AccountId is kept consistent
        station.AccountId = accountId;

        await _stationsService.UpdateStationAsync(station);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var station = await _stationsService.GetStationByIdAsync(id);
        if (station == null || station.AccountId != accountId)
            return NotFound();

        await _stationsService.DeleteStationAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCompleted(string id, string episodeId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var station = await _stationsService.GetStationByIdAsync(id);
        if (station == null || station.AccountId != accountId)
            return NotFound();

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        if (string.IsNullOrEmpty(activeId))
        {
            var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
            activeId = profiles?.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrEmpty(activeId))
            return BadRequest("No active profile found.");

        await _stationsService.MarkEpisodeCompletedAsync(id, episodeId, activeId);
        
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HttpPost]
    public async Task<IActionResult> UpdateSchedule(string id, [FromBody] StationSchedule schedule)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();
        Console.WriteLine($"DEBUG Web UpdateSchedule: Feeds.Count = {schedule?.Feeds?.Count}");
        await _stationsService.UpdateScheduleAsync(id, schedule);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCompletedUntil(string id, string episodeId)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var station = await _stationsService.GetStationByIdAsync(id);
        if (station == null || station.AccountId != accountId)
            return NotFound();

        var activeId = HttpContext.Request.Cookies["Ohrbob_ActiveProfileId"];
        if (string.IsNullOrEmpty(activeId))
        {
            var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId);
            activeId = profiles?.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrEmpty(activeId))
            return BadRequest("No active profile found.");

        await _stationsService.MarkEpisodeCompletedUntilAsync(id, episodeId, activeId);
        
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(string id, string name)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Station name cannot be empty.");

        var existingStation = await _stationsService.GetStationByIdAsync(id);
        if (existingStation == null || existingStation.AccountId != accountId)
            return NotFound();

        existingStation.Name = name;
        await _stationsService.UpdateStationAsync(existingStation);

        return Ok(new { name = existingStation.Name });
    }
}