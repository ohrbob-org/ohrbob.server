using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
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

app.Run();

public partial class Program { }
