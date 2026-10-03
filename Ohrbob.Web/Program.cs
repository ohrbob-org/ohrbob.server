using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DotNetEnv;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

 using Microsoft.AspNetCore.DataProtection;

Env.NoClobber().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/DataProtection-Keys"))
    .SetApplicationName("OhrbobWeb");

builder.Services.AddRazorPages();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});


// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient(nameof(OhrbobApiService))
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();

        // Disable certificate checking ONLY in Development
        if (builder.Environment.IsDevelopment())
        {
            handler.ServerCertificateCustomValidationCallback = 
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    });
builder.Services.AddSingleton<AccountApiService>();
builder.Services.AddSingleton<StationsApiService>();
builder.Services.AddSingleton<ProfilesApiService>();
builder.Services.AddHttpClient<PodcastIndexApiService>();
builder.Services.Configure<OhrbobApiOptions>(builder.Configuration.GetSection("OhrbobApi"));


builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Index";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddOpenIdConnect("Nextcloud", options =>
    {
        var oidc = builder.Configuration.GetSection("Oidc:Nextcloud");
        options.Authority = oidc["Authority"];
        options.ClientId = oidc["ClientId"];
        options.ClientSecret = oidc["ClientSecret"];
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.ClaimActions.MapJsonKey("picture", "picture");
        options.TokenValidationParameters.NameClaimType = "name";
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.CallbackPath = "/signin-nextcloud";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = CreateOrGetAccountAsync,
            OnRedirectToIdentityProvider = context =>
            {
                if (context.Request.Scheme == "https")
                {
                    context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri
                        .Replace("http://", "https://");
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddOpenIdConnect("Google", options =>
    {
        var oidc = builder.Configuration.GetSection("Oidc:Google");
        options.Authority = oidc["Authority"];
        options.ClientId = oidc["ClientId"];
        options.ClientSecret = oidc["ClientSecret"];
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.ClaimActions.MapJsonKey("picture", "picture");
        options.TokenValidationParameters.NameClaimType = "name";
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CallbackPath = "/signin-google";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = CreateOrGetAccountAsync,
            OnRedirectToIdentityProvider = context =>
            {
                if (context.Request.Scheme == "https")
                {
                    context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri
                        .Replace("http://", "https://");
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});


var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

async Task CreateOrGetAccountAsync(TokenValidatedContext context)
{
    var principal = context.Principal;
    var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? principal?.FindFirst("sub")?.Value;
    var name = principal?.FindFirst(ClaimTypes.Name)?.Value ?? principal?.FindFirst("name")?.Value;
    var email = principal?.FindFirst(ClaimTypes.Email)?.Value;
    var picture = principal?.FindFirst("picture")?.Value;
    var idp = principal?.FindFirst("idp")?.Value;

    var issuer = principal?.FindFirst(JwtRegisteredClaimNames.Iss)?.Value 
                 ?? principal?.FindFirst(ClaimTypes.AuthenticationMethod)?.Value;

    var accountService = context.HttpContext.RequestServices.GetRequiredService<AccountApiService>();

    // Perform post-login tasks (e.g., sync user record, audit logging)
    
    var account = await accountService.CreateOrUpdateAccountAsync(name, email, picture, issuer, userId);
    if (context.Principal?.Identity is ClaimsIdentity identity && account != null)
    {
        identity.AddClaim(new Claim(Consts.OhrbobAccountIdClaim, account.Id));
        if (!string.IsNullOrEmpty(account.LastUsedProfileId))
        {
            context.HttpContext.Response.Cookies.Append("Ohrbob_ActiveProfileId", account.LastUsedProfileId, new CookieOptions { HttpOnly = true, Secure = true, Expires = DateTimeOffset.UtcNow.AddDays(30) });
        }
    }
}