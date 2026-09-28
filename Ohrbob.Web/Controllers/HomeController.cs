using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace Ohrbob.Web.Controllers;

public class HomeController : Controller
{
    [AllowAnonymous]
    public IActionResult Index()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [AllowAnonymous]
    public IActionResult Login(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            return RedirectToAction(nameof(Index));
        }
        return Challenge(new AuthenticationProperties { RedirectUri = "/" }, provider);
    }
}