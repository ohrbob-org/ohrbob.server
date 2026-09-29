using System.Text.Json;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;

namespace Ohrbob.Api.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static void MapMyHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/_health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = JsonSerializer.Serialize(new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description
                    })
                });
                await context.Response.WriteAsync(result);
            }
        }).RequireAuthorization(Consts.GetHealthPolicy, ApiKeyDefaults.AuthenticationScheme);
    }
    
    public static void AddMyHealthChecks(this IServiceCollection services, IConfiguration configuration , Action<HealthOptions>? optionsOverride)
    {
        var options = new HealthOptions()
        {
            HealthChecksBuilder = services.AddHealthChecks()
        };
        if (optionsOverride != null)
            optionsOverride.Invoke(options);

        var serverConfig = ApiServerConfiguration.GetFromConfig(configuration);
        options.HealthChecksBuilder.AddCheck("api_version", () => HealthCheckResult.Healthy(serverConfig.Version));
        options.HealthChecksBuilder.AddCheck<MongoHealthCheck>("mongodb");
    }
    
    public class HealthOptions
    {
        public bool IncludeMongoHealthCheck { get; set; } = false;
        public required IHealthChecksBuilder HealthChecksBuilder;
    }
    
    public class MongoHealthCheck : IHealthCheck
    {
        private readonly MongoDbContext _dbContext;

        public MongoHealthCheck(MongoDbContext dbContext)
        {

            _dbContext = dbContext;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _dbContext.GetDatabase()!.RunCommandAsync((Command<BsonDocument>)"{ping:1}", cancellationToken: cancellationToken);
                return HealthCheckResult.Healthy("OK");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("ERR", ex);
            }
        }
    }
}