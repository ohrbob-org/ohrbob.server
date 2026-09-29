using System.Security.Claims;
using System.Text;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Authorization;
using Ohrbob.Api.Configuration;

namespace Ohrbob.Api.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyApiKeysAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        ApiServerConfiguration config = ApiServerConfiguration.GetFromConfig(configuration);

        services.AddAuthentication()
            .AddApiKeyInHeaderOrQueryParams<ApiKeyProvider>(options =>
            {
                options.Realm = "realm";
                options.KeyName = "X-API-KEY";
            });
        services.AddAuthorization(options =>
        {
            foreach (var p in Consts.AllPolicies)
            {
                options.AddPolicy(p.Name, configure =>
                {
                    foreach (var scope in p.Scopes)
                    {
                        configure.RequireAssertion(ctx =>
                            ctx.User.HasClaim(c =>
                                c.Type == Consts.ScopeClaim && c.Value == scope));
                    }
                });
            }


            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }
}

public class ApiKeyProvider : IApiKeyProvider
{
    private readonly ILogger<IApiKeyProvider> _logger;
    private readonly IConfiguration _configuration;
    
    public ApiKeyProvider(ILogger<IApiKeyProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }
    private ConfigApiKey? GetConfigApiKey(string key)
    {
        var section = _configuration.GetSection("ApiKeys");
        var configApiKey = (section.Get<ConfigApiKey[]>() ?? Array.Empty<ConfigApiKey>()).FirstOrDefault(x => x.Key == key);
        return configApiKey;
    }
    public Task<IApiKey?> ProvideAsync(string key)
    {
        try
        {
            var claims = new List<Claim>();
            var scopes = GetConfigApiKey(key);
            if (scopes == null)
            {
                return Task.FromResult<IApiKey?>(null);
            }

            return Task.FromResult<IApiKey?>(new ApiKey(scopes.Key, scopes.Name, scopes.GetClaims()));
        }
        catch (System.Exception exception)
        {
            _logger.LogError(exception, exception.Message);
            throw;
        }
    }
}