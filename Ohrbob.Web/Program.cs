using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DotNetEnv;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

 using Microsoft.AspNetCore.DataProtection;

Env.NoClobber().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")))
    .SetApplicationName("OhrbobWeb");

builder.Services.AddRazorPages();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});


builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<Ohrbob.Web.Filters.OnboardingFilter>();
});
HttpMessageHandler CreateHttpHandler()
{
    var handler = new HttpClientHandler();

    // Disable certificate checking ONLY in Development (self-signed dev certs)
    if (builder.Environment.IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }

    return handler;
}
builder.Services.AddHttpClient(nameof(OhrbobApiService))
    .ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
builder.Services.AddSingleton<AccountApiService>();
builder.Services.AddSingleton<StationsApiService>();
builder.Services.AddSingleton<ProfilesApiService>();
builder.Services.AddSingleton<ApiKeysApiService>();
builder.Services.AddHttpClient<PodcastIndexApiService>();
builder.Services.Configure<OhrbobApiOptions>(builder.Configuration.GetSection("OhrbobApi"));
builder.Services.AddSingleton<ChangelogService>();
builder.Services.AddSingleton<BackendVersionCache>();
builder.Services.AddHostedService<VersionPollingService>();
builder.Services.AddHttpClient(VersionPollingService.ApiClientName, (sp, c) =>
{
    var api = sp.GetRequiredService<IOptions<OhrbobApiOptions>>().Value;
    api.Initialize();
    c.BaseAddress = new Uri(api.BaseUrl);
}).ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
builder.Services.AddHttpClient(VersionPollingService.PlayerClientName, (sp, c) =>
{
    var api = sp.GetRequiredService<IOptions<OhrbobApiOptions>>().Value;
    api.Initialize();
    c.BaseAddress = new Uri(api.PlayerApiUrl);
}).ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
builder.Services.Configure<AppVersionOptions>(builder.Configuration.GetSection(AppVersionOptions.SectionName));
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection(LegalOptions.SectionName));
builder.Services.Configure<HtmxNavigationOptions>(builder.Configuration.GetSection(HtmxNavigationOptions.SectionName));
builder.Services.Configure<StreamOptions>(builder.Configuration.GetSection(StreamOptions.SectionName));


builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
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
            OnTokenValidated = async context =>
            {
                context.Properties.IsPersistent = true;
                await CreateOrGetAccountAsync(context);
            },
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
            OnTokenValidated = async context =>
            {
                context.Properties.IsPersistent = true;
                await CreateOrGetAccountAsync(context);
            },
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

var githubSection = builder.Configuration.GetSection("Oidc:GitHub");
var githubId = githubSection["ClientId"];
var githubSecret = githubSection["ClientSecret"];
if (!string.IsNullOrWhiteSpace(githubId) && !string.IsNullOrWhiteSpace(githubSecret))
{
    builder.Services.AddAuthentication().AddGitHub("GitHub", options =>
    {
        options.ClientId = githubId!;
        options.ClientSecret = githubSecret!;
        options.CallbackPath = "/signin-github";
        options.Scope.Add("user:email");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
        options.ClaimActions.MapJsonKey("picture", "avatar_url");
        options.SaveTokens = true;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context =>
            {
                if (context.Identity != null && string.IsNullOrEmpty(context.Identity.FindFirst("iss")?.Value))
                {
                    context.Identity.AddClaim(new Claim("iss", "https://github.com"));
                }
                return Task.CompletedTask;
            },
            OnTicketReceived = async context =>
            {
                context.Properties.IsPersistent = true;
                await CreateOrGetAccountCore(context.Principal, context.HttpContext, context.Scheme.Name);
            },
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/Home/Error");
                context.HandleResponse();
                return Task.CompletedTask;
            },
            OnRedirectToAuthorizationEndpoint = context =>
            {
                if (context.HttpContext.Request.Scheme == "https")
                {
                    context.RedirectUri = context.RedirectUri.Replace("http://", "https://");
                }
                // NOTE: RedirectContext<OAuthOptions> does NOT derive from HandleRequestContext,
                // so there is no HandleResponse() here (verified: CS1061 on .NET 10). Unlike
                // OnRemoteFailure, no HandleResponse call is possible or needed: OAuthHandler.
                // HandleChallengeAsync performs no redirect after this event, so this
                // Response.Redirect (same as the framework's default handler) is the single,
                // terminal redirect — no double-redirect risk.
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});


var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is HttpRequestException || 
                               ex.GetBaseException() is HttpRequestException || 
                               ex.GetBaseException() is System.Net.Sockets.SocketException ||
                               (ex is HttpRequestException hx && hx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) ||
                               (ex.GetBaseException() is HttpRequestException bhx && bhx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable))
    {
        context.Response.Redirect("/Home/ServiceUnavailable");
    }
});

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
app.MapGet("/health", (BackendVersionCache cache, IOptions<AppVersionOptions> v) =>
{
    var up = cache.ApiReachable || cache.PlayerReachable;
    return Results.Ok(new
    {
        status = up ? "Healthy" : "Degraded",
        version = v.Value.Version,
        tag = v.Value.Tag,
        checks = new { api = cache.ApiReachable ? "Healthy" : "Unhealthy", player = cache.PlayerReachable ? "Healthy" : "Unhealthy" }
    });
}).AllowAnonymous();
app.Run();

async Task CreateOrGetAccountAsync(TokenValidatedContext context)
{
    await CreateOrGetAccountCore(context.Principal, context.HttpContext, context.Scheme.Name);
}

async Task CreateOrGetAccountCore(ClaimsPrincipal? principal, HttpContext httpContext, string schemeName)
{
    var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? principal?.FindFirst("sub")?.Value
                 ?? principal?.FindFirst("id")?.Value;
    var name = principal?.FindFirst(ClaimTypes.Name)?.Value ?? principal?.FindFirst("name")?.Value;
    var email = principal?.FindFirst(ClaimTypes.Email)?.Value;
    var picture = principal?.FindFirst("picture")?.Value;
    var idp = principal?.FindFirst("idp")?.Value;

    var issuer = principal?.FindFirst(JwtRegisteredClaimNames.Iss)?.Value 
                 ?? principal?.FindFirst("iss")?.Value
                 ?? (schemeName == "GitHub" ? "https://github.com" : principal?.FindFirst(ClaimTypes.AuthenticationMethod)?.Value);

    var accountService = httpContext.RequestServices.GetRequiredService<AccountApiService>();

    // Perform post-login tasks (e.g., sync user record, audit logging)
    
    var account = await accountService.CreateOrUpdateAccountAsync(name, email, picture, issuer, userId);
    if (principal?.Identity is ClaimsIdentity identity && account != null)
    {
        identity.AddClaim(new Claim(Consts.OhrbobAccountIdClaim, account.Id));
        if (!string.IsNullOrEmpty(account.LastUsedProfileId))
        {
            httpContext.Response.Cookies.Append("Ohrbob_ActiveProfileId", account.LastUsedProfileId, new CookieOptions { HttpOnly = true, Secure = true, Expires = DateTimeOffset.UtcNow.AddDays(30) });
        }
    }
}
public partial class Program { }
