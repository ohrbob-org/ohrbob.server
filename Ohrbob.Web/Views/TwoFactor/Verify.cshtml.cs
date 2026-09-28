using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ohrbob.Web.Data;

namespace Piggy.Pages.TwoFactor;

public class VerifyModel : PageModel
{
    private readonly ITOTPSecretRepository _totpRepo;

    public VerifyModel(ITOTPSecretRepository totpRepo)
    {
        _totpRepo = totpRepo;
    }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnPostAsync(string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "";

        if (string.IsNullOrEmpty(code))
        {
            ErrorMessage = "Please enter your authenticator code.";
            return Page();
        }

        var secret = await _totpRepo.GetByUserIdAsync(userId);
        if (secret == null)
        {
            return RedirectToPage("/TwoFactor/Setup");
        }

        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret.Secret));
        if (!totp.VerifyTotp(code, out _))
        {
            ErrorMessage = "Invalid code. Try again.";
            return Page();
        }

        var claims = new List<Claim>(User.Claims);
        claims.Add(new Claim("2fa_verified", "true"));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return RedirectToPage("/Index");
    }
}
