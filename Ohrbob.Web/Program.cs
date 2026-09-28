using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Ohrbob.Web.Data;
using Ohrbob.Web.Middleware;
using Ohrbob.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<ITOTPSecretRepository, TOTPSecretRepository>();


builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Index";
    })
    .AddOpenIdConnect("Nextcloud", options =>
    {
        var oidc = builder.Configuration.GetSection("Oidc:Nextcloud");
        options.Authority = oidc["Authority"];
        options.ClientId = oidc["ClientId"];
        options.ClientSecret = oidc["ClientSecret"];
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.CallbackPath = "/signin-nextcloud";
        options.Events = new OpenIdConnectEvents
        {
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
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CallbackPath = "/signin-google";
        options.Events = new OpenIdConnectEvents
        {
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
app.UseTwoFactor();
app.UseAuthorization();
app.MapRazorPages();
app.MapMethods("/logout", new[] { "POST", "GET" }, () =>
{
    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, 
        new[] { CookieAuthenticationDefaults.AuthenticationScheme });
});
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
