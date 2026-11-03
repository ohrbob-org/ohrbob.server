using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

public class MoveFeedRequest
{
    public string SourceStationId { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string FeedTitle { get; set; } = string.Empty;
    public string? TargetStationId { get; set; }
    public bool CreateNewStation { get; set; }
    public string? NewStationName { get; set; }
}

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

        var allStations = await _stationsService.GetStationsByAccountIdAsync(accountId) ?? new();
        ViewBag.OtherStations = allStations.Where(s => s.Station.Id != id).ToList();

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

        // Ensure AccountId and the share code are kept consistent
        station.AccountId = accountId;
        station.ShareCode = existingStation.ShareCode;

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

        var station = await _stationsService.GetStationByIdAsync(id);
        if (station == null || station.AccountId != accountId) return NotFound();

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
    public async Task<IActionResult> ClearCompleted(string id, string episodeId)
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

        await _stationsService.ClearEpisodeCompletedAsync(id, episodeId, activeId);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPlayhead(string id, string episodeId, int positionSeconds)
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

        await _stationsService.SetEpisodePlayheadAsync(id, episodeId, activeId, Math.Max(0, positionSeconds));
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCompletedUntil(string id, string episodeId)
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

        await _stationsService.ClearEpisodeCompletedUntilAsync(id, episodeId, activeId);
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSettings(string id, string name, bool isAging, bool isPublic)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Station name cannot be empty.");

        var existingStation = await _stationsService.GetStationByIdAsync(id);
        if (existingStation == null || existingStation.AccountId != accountId)
            return NotFound();

        existingStation.Name = name;
        existingStation.IsAging = isAging;
        existingStation.IsPublic = isPublic;
        await _stationsService.UpdateStationAsync(existingStation);

        return Ok(new { name = existingStation.Name, isAging = existingStation.IsAging, isPublic = existingStation.IsPublic });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFeed([FromBody] MoveFeedRequest request)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.FeedUrl) ||
            (!request.CreateNewStation && string.IsNullOrWhiteSpace(request.TargetStationId)))
        {
            return ToastError("No destination selected.", 422);
        }

        var source = await _stationsService.GetStationByIdAsync(request.SourceStationId);
        if (source == null || source.AccountId != accountId) return NotFound();

        var sourceEntry = source.Schedule?.Feeds?.FirstOrDefault(f => f.Url == request.FeedUrl);
        if (sourceEntry == null) return ToastError("Feed is not in this station.", 422);

        string destName;
        try
        {
            // Destination first — a failed move never loses the feed.
            if (request.CreateNewStation)
            {
                destName = string.IsNullOrWhiteSpace(request.NewStationName)
                    ? request.FeedTitle
                    : request.NewStationName!.Trim();

                var newStation = new Station
                {
                    AccountId = accountId,
                    Name = destName,
                    Feeds = new[] { request.FeedUrl },
                    Schedule = new StationSchedule
                    {
                        Feeds = new List<StationFeedSchedule>
                        {
                            new StationFeedSchedule
                            {
                                Url = request.FeedUrl,
                                SortOrder = sourceEntry.SortOrder,
                                EpisodesToPlay = sourceEntry.EpisodesToPlay
                            }
                        }
                    }
                };

                var created = await _stationsService.CreateStationAsync(accountId, newStation);

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
            }
            else
            {
                if (request.TargetStationId == request.SourceStationId)
                    return ToastError("A feed cannot be moved to its own station.", 422);

                var target = await _stationsService.GetStationByIdAsync(request.TargetStationId!);
                if (target == null || target.AccountId != accountId) return NotFound();

                if (target.Schedule == null) target.Schedule = new StationSchedule();
                if (target.Schedule.Feeds == null) target.Schedule.Feeds = new List<StationFeedSchedule>();
                if (target.Schedule.Feeds.Any(f => f.Url == request.FeedUrl))
                    return ToastError("That station already has this podcast.", 422);

                target.Schedule.Feeds.Add(new StationFeedSchedule
                {
                    Url = request.FeedUrl,
                    SortOrder = sourceEntry.SortOrder,
                    EpisodesToPlay = sourceEntry.EpisodesToPlay
                });
                target.Feeds = target.Schedule.Feeds.Select(f => f.Url).ToArray();
                await _stationsService.UpdateStationAsync(target);
                destName = target.Name;
            }
        }
        catch (LimitExceededException ex)
        {
            return ToastError(ex.Message, 422);
        }

        try
        {
            source.Schedule!.Feeds.Remove(sourceEntry);
            source.Feeds = source.Schedule.Feeds.Select(f => f.Url).ToArray();
            await _stationsService.UpdateStationAsync(source);
        }
        catch (Exception)
        {
            return ToastError("Feed was added to the destination but could not be removed from the source.", 422);
        }

        return ToastSuccess($"Moved '{request.FeedTitle}' to '{destName}'");
    }

    private const long MaxOpmlFileBytes = 2 * 1024 * 1024;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportOpml(string id, IFormFile opml)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var station = await _stationsService.GetStationByIdAsync(id);
        if (station == null || station.AccountId != accountId) return NotFound();

        if (opml == null || opml.Length == 0)
            return ToastError("Please choose an OPML file.", 422);

        if (opml.Length > MaxOpmlFileBytes)
            return ToastError("That OPML file is too large (max 2 MB).", 422);

        List<string> urls;
        try
        {
            await using var stream = opml.OpenReadStream();
            urls = OpmlParser.ExtractFeedUrls(stream);
        }
        catch
        {
            return ToastError("That file is not a valid OPML document.", 422);
        }

        if (urls.Count == 0)
            return ToastError("No feeds were found in that OPML file.", 422);

        var result = await _stationsService.AddFeedsAsync(id, urls);
        if (result == null)
            return ToastError("The feeds could not be imported.", 422);

        var message = BuildImportMessage(result);
        return result.Added.Count > 0 ? ToastSuccess(message) : ToastError(message, 422);
    }

    private static string BuildImportMessage(Ohrbob.Server.Shared.Requests.FeedImportResultDto result)
    {
        var parts = new List<string>();
        if (result.Added.Count > 0) parts.Add($"Imported {result.Added.Count}");
        if (result.AlreadyPresent.Count > 0) parts.Add($"{result.AlreadyPresent.Count} already present");
        if (result.Invalid.Count > 0) parts.Add($"{result.Invalid.Count} could not be read");
        if (result.NotAddedDueToLimit.Count > 0) parts.Add($"{result.NotAddedDueToLimit.Count} skipped (feed limit reached)");

        if (parts.Count == 0) return "No feeds were imported.";
        return string.Join(", ", parts) + ".";
    }

    private IActionResult ToastSuccess(string message)
    {
        Response.Headers["HX-Trigger"] = System.Text.Json.JsonSerializer.Serialize(
            new { showToast = new { message, type = "success" } });
        return StatusCode(204);
    }

    private IActionResult ToastError(string message, int status)
    {
        Response.Headers["HX-Trigger"] = System.Text.Json.JsonSerializer.Serialize(
            new { showToast = new { message, type = "danger" } });
        return StatusCode(status, message);
    }
}