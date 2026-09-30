using AspNetCore.Authentication.ApiKey;
using DotNetEnv;
using Ohrbob.Api;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Filters;
using Microsoft.OpenApi;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Api.Services;

Env.Load();
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var mongoDbSettings = configuration.GetSection("MongoDbData").Get<MongoDbSettings>()!;
var logsDbSettings = configuration.GetSection("MongoDbLogs").Get<MongoDbSettings>()!;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Logger(lc =>
    {
        lc.Filter.ByIncludingOnly(Matching.WithProperty(LogAuditExtensions.AuditPropertyName))
            .WriteTo.MongoDBBson(databaseUrl: logsDbSettings.ConnectionString,
                collectionName: logsDbSettings.DatabaseName);
    }).CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        var requirement = new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("ApiKey", document),
                new List<string>()
            }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
#pragma warning disable CS8602
        document.Components.SecuritySchemes.Add("ApiKey", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "X-API-KEY",
            In = ParameterLocation.Header,
            Description = "API Key authentication"
        });
#pragma warning restore CS8602

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(requirement);
        return Task.CompletedTask;
    });
});
builder.Services.AddMyHealthChecks(configuration, options => {});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

builder.Services.AddMyApiKeysAuthorization(configuration);

var apiConfig = ApiServerConfiguration.GetFromConfig(configuration);
apiConfig.Validate();

builder.Services.AddSingleton(apiConfig);

builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbData"));
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<AccountService>();

builder.Services.AddControllers();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.UseDefaultFiles();

app.MapGet("/", () => "Ok");


if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        var versionName = $"v{apiConfig.Version}";
        ConfigureScalarOptions(options, apiConfig, versionName);
        
        options.AddDocument(versionName, $"Version {apiConfig.Version}", $"/openapi/{versionName}.json");
        
    }).AllowAnonymous();

}
else
{
    app.MapOpenApi(); // Serve openapi.json
    app.MapScalarApiReference().RequireAuthorization(Consts.GetScalarPolicy, ApiKeyDefaults.AuthenticationScheme);

}

app.MapControllers();
app.MapMyHealthChecks();
app.Run();



static void ConfigureScalarOptions(ScalarOptions options, ApiServerConfiguration config, string versionName)
{
    options.DisableAgent();
    options.DisableMcp();
    options.DisableTelemetry();
    options.DisableDefaultFonts();
    options.HideModels = false;
    options.HideClientButton = true;
    options.HideDeveloperTools();
        
    options.WithTitle($"Api")
        .WithTheme(ScalarTheme.DeepSpace)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithOpenApiRoutePattern($"/openapi/{versionName}.json");
}
