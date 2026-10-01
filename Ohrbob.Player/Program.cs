using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Ohrbob.Player.Configuration;
using Ohrbob.Player.Services;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// MongoDB Setup
var mongoDbSettings = configuration.GetSection("MongoDbData").Get<MongoDbSettings>();
if (mongoDbSettings != null)
{
    builder.Services.AddSingleton(new MongoDbContext(mongoDbSettings.ConnectionString, mongoDbSettings.DatabaseName));
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PlayerContextService>();
builder.Services.AddSingleton<ListenStateService>();

builder.Services.AddControllers();

// Auth setup (JWT via OIDC token passed from client)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var oidc = configuration.GetSection("Oidc:Nextcloud"); // Or other provider
        options.Authority = oidc["Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false, // Configurable per deployment
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
