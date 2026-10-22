using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Filters;

public class OnboardingFilter : IAsyncActionFilter
{
    private readonly AccountApiService _accountService;

    public OnboardingFilter(AccountApiService accountService)
    {
        _accountService = accountService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        
        // Skip if not authenticated
        if (user.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        // Allow static files, auth endpoints, and the onboarding controller itself to execute
        var controllerName = context.RouteData.Values["controller"]?.ToString();
        var actionName = context.RouteData.Values["action"]?.ToString();
        
        if (string.Equals(controllerName, "Onboarding", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(controllerName, "Home", StringComparison.OrdinalIgnoreCase) && 
            (string.Equals(actionName, "Logout", StringComparison.OrdinalIgnoreCase) || string.Equals(actionName, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(actionName, "ServiceUnavailable", StringComparison.OrdinalIgnoreCase) || string.Equals(actionName, "Legal", StringComparison.OrdinalIgnoreCase)))
        {
            await next();
            return;
        }

        var accountId = user.FindFirst(Consts.OhrbobAccountIdClaim)?.Value;
        if (!string.IsNullOrEmpty(accountId))
        {
            try 
            {
                var account = await _accountService.GetAccountAsync(accountId);
                if (account != null && account.NeedsOnboarding)
                {
                    // Remember where the person was heading so onboarding can send them back
                    // (e.g. /connect/app) once it completes.
                    var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;

                    // Redirect to onboarding via a full redirect, not HTMX partial
                    if (context.HttpContext.Request.Headers.ContainsKey("HX-Request"))
                    {
                        context.HttpContext.Response.Headers["HX-Redirect"] =
                            QueryHelpers.AddQueryString("/Onboarding", "returnUrl", returnUrl);
                        context.Result = new OkResult();
                        return;
                    }

                    context.Result = new RedirectToActionResult("Index", "Onboarding", new { returnUrl });
                    return;
                }
            }
            catch (Exception)
            {
                // If API is down, let it pass so the global error handler can catch it, or redirect to ServiceUnavailable.
                // It will naturally bubble up to the ExceptionHandler middleware.
            }
        }

        await next();
    }
}