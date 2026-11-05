using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace Ohrbob.E2ETests.Infrastructure;

public class PlaywrightWebApplicationFactory : WebApplicationFactory<Program>
{
    private IHost? _host;
    public string ServerAddress { get; private set; } = string.Empty;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = builder.Build();
        
        builder.ConfigureWebHost(webHostBuilder =>
        {
            webHostBuilder.UseKestrel();
            webHostBuilder.UseUrls("http://127.0.0.1:0");
        });

        _host = builder.Build();

        // Do not register OS file watchers for the served host. Four dozen tests each build
        // their own host in one process; Razor's asp-append-version watchers would otherwise
        // exceed the per-user inotify instance limit.
        var environment = _host.Services.GetRequiredService<IWebHostEnvironment>();
        environment.WebRootFileProvider = new NoWatchFileProvider(environment.WebRootFileProvider);
        environment.ContentRootFileProvider = new NoWatchFileProvider(environment.ContentRootFileProvider);

        _host.Start();

        var server = _host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        
        ServerAddress = addresses?.Addresses.FirstOrDefault() ?? "http://localhost";

        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "OhrbobApi:BaseUrl", "http://127.0.0.1:5001/api" },
                { "OhrbobApi:PlayerApiUrl", "http://127.0.0.1:5002" },
                { "OhrbobApi:PlayerPublicUrl", "http://127.0.0.1:5002" },
                // OhrbobApi:ApiKey and the PodcastIndex credentials come from the top-level `.env`
                // (loaded by Program.cs); never hard-code secrets here.
                { "Oidc:Nextcloud:Authority", "https://localhost" },
                { "Oidc:Nextcloud:ClientId", "dummy-client-id" },
                { "Oidc:Nextcloud:ClientSecret", "dummy-client-secret" },
                { "Oidc:Google:Authority", "https://localhost" },
                { "Oidc:Google:ClientId", "dummy-client-id" },
                { "Oidc:Google:ClientSecret", "dummy-client-secret" },
                // The deployed app gets this from the top-level `.env` (env_file), but Program.cs
                // only loads `{CWD}/.env` without traversing parent directories, so the test host
                // would otherwise fall back to appsettings.json's `false` and render no terms checkbox.
                { "Legal:Show", "true" }
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _host?.Dispose();
        }
    }
}