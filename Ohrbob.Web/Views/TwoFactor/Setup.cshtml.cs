using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ohrbob.Server.Shared.TwoFactor;
using Ohrbob.Web.Data;
using QRCoder;

namespace Piggy.Pages.TwoFactor;

public class SetupModel : PageModel
{
    private readonly ITOTPSecretRepository _totpRepo;

    public SetupModel(ITOTPSecretRepository totpRepo)
    {
        _totpRepo = totpRepo;
    }

    public string? Secret { get; set; }
    public string? QrCodeSvg { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(bool? disabled)
    {
        if (disabled == true)
            SuccessMessage = "Two-factor authentication has been disabled.";

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "";
        var existing = await _totpRepo.GetByUserIdAsync(userId);
        if (existing == null)
        {
            var key = OtpNet.KeyGeneration.GenerateRandomKey(20);
            Secret = OtpNet.Base32Encoding.ToString(key);
            var uri = GenerateOtpUri(userId, Secret);
            QrCodeSvg = GenerateSvg(uri);
        }
    }

    public async Task<IActionResult> OnPostAsync(string secret, string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "";

        if (string.IsNullOrEmpty(code))
        {
            ErrorMessage = "Please enter the code from your authenticator app.";
            var uri = GenerateOtpUri(userId, secret);
            QrCodeSvg = GenerateSvg(uri);
            Secret = secret;
            return Page();
        }

        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret));
        if (!totp.VerifyTotp(code, out _))
        {
            ErrorMessage = "Invalid code. Make sure your authenticator app is set up correctly.";
            var uri = GenerateOtpUri(userId, secret);
            QrCodeSvg = GenerateSvg(uri);
            Secret = secret;
            return Page();
        }

        await _totpRepo.CreateOrUpdateAsync(new TOTPSecret
        {
            UserId = userId,
            Secret = secret,
            CreatedAt = DateTime.UtcNow
        });

        SuccessMessage = "Two-factor authentication is now enabled.";
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "";
        await _totpRepo.DeleteAsync(userId);
        return RedirectToPage("Setup", new { disabled = true });
    }

    private static string GenerateOtpUri(string userId, string secret)
    {
        return $"otpauth://totp/Piggy:{userId}?secret={secret}&issuer=Piggy";
    }

    private static string GenerateSvg(string uri)
    {
        using var qr = new QRCodeGenerator();
        var data = qr.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(4);
    }
}
