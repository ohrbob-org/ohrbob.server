using AspNetCore.Authentication.ApiKey;
using Ohrbob.Api;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var mongoDbSettings = builder.Configuration.GetSection("MongoDbData").Get<MongoDbSettings>();
var logsDbSettings = builder.Configuration.GetSection("MongoDbLogs").Get<MongoDbSettings>();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Logger(lc =>
    {
        lc.Filter.ByIncludingOnly(Matching.WithProperty("AuditScope"))
            .WriteTo.MongoDBBson(databaseUrl: builder.Configuration.GetConnectionString("MongoDbLogs")
                                              ?? "mongodb://localhost:27017/AppLogs",
                collectionName: "logs");
    }).CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddOpenApi();
builder.Services.AddMyHealthChecks(configuration, options => {});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

builder.Services.AddMyApiKeysAuthorization(configuration);

var apiConfig = ApiServerConfiguration.GetFromConfig(configuration);
apiConfig.Validate();

builder.Services.AddSingleton(apiConfig);

builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbData"));
builder.Services.AddSingleton<MongoDbContext>();

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    builder.Services.AddOpenApi(); // Register OpenAPI schema generator
}

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
