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
using MongoDB.Bson;
using MongoDB.Driver;

Env.NoClobber().Load();
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var logsDbSettings = configuration.GetSection("MongoDbLogs").Get<MongoDbSettings>();
var dataDbSettings = configuration.GetSection("MongoDbData").Get<MongoDbSettings>();



var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console();

if (logsDbSettings != null && !string.IsNullOrEmpty(logsDbSettings.ConnectionString))
{
    loggerConfig.WriteTo.Logger(lc =>
    {
        lc.Filter.ByIncludingOnly(Matching.WithProperty(LogAuditExtensions.AuditPropertyName))
            .WriteTo.MongoDBBson(databaseUrl: logsDbSettings.ConnectionString,
                collectionName: logsDbSettings.DatabaseName);
    });
}

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();
Log.Information("Starting application");
Log.Information("LogsDb: {db}", logsDbSettings?.ConnectionString);
Log.Information("DataDb: {db}", dataDbSettings?.ConnectionString);

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
builder.Services.Configure<SubscriptionTiersOptions>(builder.Configuration.GetSection(SubscriptionTiersOptions.ConfigSection));
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<StationsService>();
builder.Services.AddSingleton<ProfilesService>();
builder.Services.AddSingleton<EpisodesService>();
builder.Services.AddSingleton<ListenStateService>();
builder.Services.AddSingleton<FeedsService>();
builder.Services.AddSingleton<SubscriptionPolicy>();
builder.Services.AddHostedService<FeedUpdateWorker>();

builder.Services.AddControllers();

builder.Services.AddProblemDetails();

var app = builder.Build();

var maxRetries = 5;
var retryDelaySeconds = 3;

// Skip the startup MongoDB probe when the app is being run by the
// OpenAPI document generator at build time (dotnet-getdocument.dll).
// There is no MongoDB available in that context, and exiting here
// fails the Docker build with MSB3073.
var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
var isOpenApiBuildGeneration = entryAssemblyName.Contains("getdocument", StringComparison.OrdinalIgnoreCase);

var mongoSettings = builder.Configuration.GetSection("MongoDbData").Get<MongoDbSettings>();
if (!isOpenApiBuildGeneration && mongoSettings != null && !string.IsNullOrEmpty(mongoSettings.ConnectionString))
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            var db = dbContext.GetDatabase();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            if (db != null)
            {
                db.RunCommand((Command<BsonDocument>)"{ping:1}", cancellationToken: cts.Token);
                Log.Information("Successfully connected to MongoDB (Data).");
            }

            if (logsDbSettings != null && !string.IsNullOrEmpty(logsDbSettings.ConnectionString))
            {
                var logsClient = new MongoClient(logsDbSettings.ConnectionString);
                logsClient.GetDatabase(logsDbSettings.DatabaseName).RunCommand((Command<BsonDocument>)"{ping:1}", cancellationToken: cts.Token);
                Log.Information("Successfully connected to MongoDB (Logs).");
            }
            
            break; // Stop retrying on success
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to connect to MongoDB {ConnectionString}. Retrying in {Delay} seconds... ({Attempt}/{MaxAttempts})", mongoSettings.ConnectionString, retryDelaySeconds, i + 1, maxRetries);
            if (i == maxRetries - 1)
            {
                Log.Fatal(ex, "Failed to connect to one or more MongoDB databases within timeout. Exiting application.");
                Log.CloseAndFlush();
                Environment.Exit(1);
            }
            Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
        }
    }
}

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
