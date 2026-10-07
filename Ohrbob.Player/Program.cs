using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using Ohrbob.Player.Configuration;
using Ohrbob.Player.Services;
using MongoDB.Driver;
using DotNetEnv;

Env.NoClobber().Load();
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
builder.Services.Configure<AppVersionOptions>(configuration.GetSection(AppVersionOptions.SectionName));

// MongoDB Setup
var mongoDbSettings = configuration.GetSection("MongoDbData").Get<MongoDbSettings>();
if (mongoDbSettings != null)
{
    builder.Services.AddSingleton(new MongoDbContext(mongoDbSettings.ConnectionString, mongoDbSettings.DatabaseName));
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PlayerContextService>();
builder.Services.AddScoped<PlaybackStrategyService>();
builder.Services.AddSingleton<ListenStateService>();

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (IOptions<AppVersionOptions> version, IServiceProvider services) =>
{
    string mongoStatus;
    var dbContext = services.GetService<MongoDbContext>();
    if (dbContext?.GetDatabase() is null)
    {
        mongoStatus = "NotConfigured";
    }
    else
    {
        try
        {
            await dbContext.GetDatabase()!.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            mongoStatus = "Healthy";
        }
        catch
        {
            mongoStatus = "Unhealthy";
        }
    }
    var overall = mongoStatus == "Unhealthy" ? "Degraded" : "Healthy";
    return Results.Ok(new
    {
        status = overall,
        version = version.Value.Version,
        tag = version.Value.Tag,
        checks = new { mongodb = mongoStatus }
    });
}).AllowAnonymous();

app.Run();

public partial class Program { }
