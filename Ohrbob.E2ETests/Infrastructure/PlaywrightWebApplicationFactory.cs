using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        _host.Start();

        var server = _host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        
        ServerAddress = addresses?.Addresses.FirstOrDefault() ?? "http://localhost";

        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication("Test")
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