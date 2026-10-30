using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class StreamController : Controller
{
    private readonly ProfilesApiService _profilesService;
    private readonly StationsApiService _stationsService;
    private readonly StreamOptions _streamOptions;

    public StreamController(ProfilesApiService profilesService, StationsApiService stationsService, IOptions<StreamOptions> streamOptions)
    {
        _profilesService = profilesService;
        _stationsService = stationsService;
        _streamOptions = streamOptions.Value;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var profiles = await _profilesService.GetProfilesByAccountIdAsync(accountId) ?? new List<Profile>();
        var stations = await _stationsService.GetStationsByAccountIdAsync(accountId) ?? new List<StationWithFeedsDto>();
        var stationNames = stations.ToDictionary(s => s.Station.Id, s => s.Station.Name);

        var model = new StreamViewModel
        {
            Profiles = profiles.Select(profile => new StreamProfileGroup
            {
                ProfileName = profile.Name,
                Stations = (profile.AllowedStationIds ?? Array.Empty<string>())
                    .Where(stationNames.ContainsKey)
                    .Select(stationId => new StreamStationLink
                    {
                        StationName = stationNames[stationId],
                        Url = _streamOptions.GetStationUrl(accountId, stationId, profile.StreamToken)
                    })
                    .ToList()
            }).ToList()
        };

        return View(model);
    }
}
