using AspNetCore.Authentication.ApiKey;
using Ohrbob.Api;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddMyHealthChecks(configuration, options => {});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

builder.Services.AddMyApiKeysAuthorization(configuration);

var apiConfig = ApiServerConfiguration.GetFromConfig(configuration);
apiConfig.Validate();

builder.Services.AddSingleton(apiConfig);

builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
builder.Services.AddSingleton<MongoDbContext>();

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    builder.Services.AddOpenApi(); // Register OpenAPI schema generator
}

var app = builder.Build();

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
    app.MapScalarApiReference().RequireAuthorization(Consts.GetScalarScope, ApiKeyDefaults.AuthenticationScheme);

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
