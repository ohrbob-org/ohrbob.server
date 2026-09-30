using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

[Authorize]
public class StationsController : Controller
{
    private readonly StationsApiService _stationsService;

    public StationsController(StationsApiService stationsService)
    {
        _stationsService = stationsService;
    }

    private string? GetAccountId() => User.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;

    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        var stations = await _stationsService.GetStationsByAccountIdAsync(accountId);
        return View(stations ?? new List<Station>());
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

        await _stationsService.CreateStationAsync(accountId, station);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var accountId = GetAccountId();
        if (string.IsNullOrEmpty(accountId)) return Forbid();

        // In a real app we'd verify the station belongs to this accountId first before deleting
        await _stationsService.DeleteStationAsync(id);
        return RedirectToAction(nameof(Index));
    }
}