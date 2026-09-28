using Ohrbob.Web.Data;

namespace Ohrbob.Web.Middleware;

public class TwoFactorMiddleware
{
    private readonly RequestDelegate _next;

    public TwoFactorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITOTPSecretRepository totpRepo)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var disable2fa = context.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetValue<bool>("Disable2FA");

            if (!disable2fa)
            {
                var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
                var isExempt = path.StartsWith("/twofactor") || path == "/logout";

                if (!isExempt)
                {
                    var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? context.User.Identity?.Name ?? "";

                    var has2fa = await totpRepo.GetByUserIdAsync(userId);
                    if (has2fa != null)
                    {
                        var isVerified = context.User.HasClaim("2fa_verified", "true");
                        if (!isVerified)
                        {
                            context.Response.Redirect("/TwoFactor/Verify");
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }
}

public static class TwoFactorMiddlewareExtensions
{
    public static IApplicationBuilder UseTwoFactor(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TwoFactorMiddleware>();
    }
}
