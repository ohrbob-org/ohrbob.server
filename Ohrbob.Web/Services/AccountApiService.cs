using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public class AccountApiService : OhrbobApiService
{
    public AccountApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options, IHttpClientFactory clientFactory) : base(logger, options, clientFactory)
    {
    }

    public async Task<Account?> CreateOrUpdateAccountAsync(string name, string email, string picture, string identityProvider,
        string userId)
    {
        using var request = BuildRequest(HttpMethod.Post, "/account/create");

        request.Content = new StringContent(JsonSerializer.Serialize(new CreateAccountRequest()
        {
            Name = name,
            Email = email,
            ProfilePicture = picture,
            IdentityProvider = new IdpUser() { Issuer = identityProvider, UserId = userId }
        }), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<Account>();
        return account;
    }

    public async Task CompleteOnboardingAsync(string accountId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/account/id/{accountId}/complete-onboarding");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task<Account?> GetAccountAsync(string accountId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/account/id/{accountId}");
        using var client =  ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Account>();
    }

    
    public async Task<Account?> AddIdentityProviderAsync(string accountId, IdpUser idpUser)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/account/idp");
        
        request.Content = new StringContent(JsonSerializer.Serialize(new IdentityProviderRequest()
        {
            AccountId = accountId,
            Issuer = idpUser.Issuer,
            UserId = idpUser.UserId
        }), Encoding.UTF8, "application/json");
        using var client =  ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Account>();
    }
    
    public async Task<Account?> DeleteIdentityProviderAsync(string accountId, IdpUser idpUser)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/account/idp");
        
        request.Content = new StringContent(JsonSerializer.Serialize(new IdentityProviderRequest()
        {
            AccountId = accountId,
            Issuer = idpUser.Issuer,
            UserId = idpUser.UserId
        }), Encoding.UTF8, "application/json");
        using var client =  ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Account>();
    }
    public async Task<Account?> SetActiveProfileAsync(string accountId, string profileId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/account/{accountId}/active-profile/{profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Account>();
    }

    public virtual async Task<Account?> SetTierAsync(string accountId, SubscriptionTier tier)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/account/id/{accountId}/tier");
        request.Content = new StringContent(JsonSerializer.Serialize(new { tier = tier.ToString() }), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Account>();
    }

    public virtual async Task<TierLimits?> GetLimitsAsync(string accountId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/account/id/{accountId}/limits");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TierLimits>();
    }
}
