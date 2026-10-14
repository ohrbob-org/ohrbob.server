using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Services;

public class PlayerContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMongoCollection<Profile> _profiles;
    private readonly IMongoCollection<ApiKey> _apiKeys;

    public PlayerContextService(IHttpContextAccessor httpContextAccessor, MongoDbContext dbContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _profiles = dbContext.GetCollection<Profile>("ProfileCollection");
        _apiKeys = dbContext.GetCollection<ApiKey>("ApiKeyCollection");
    }

    public async Task<Profile?> GetCurrentProfileAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        var accountId = context.User.FindFirst("ohrbob_account_id")?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        
        if (string.IsNullOrEmpty(accountId)) return null;

        if (!context.Request.Headers.TryGetValue("X-Stream-Token", out var streamTokenHeader))
            return null;

        var streamToken = streamTokenHeader.ToString();
        
        var filter = Builders<Profile>.Filter.And(
            Builders<Profile>.Filter.Eq(p => p.StreamToken, streamToken),
            Builders<Profile>.Filter.Eq(p => p.AccountId, accountId),
            Builders<Profile>.Filter.Eq(p => p.DeletedAt, null)
        );

        return await _profiles.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<Profile?> GetProfileByStreamTokenAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        string? streamToken = null;
        
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authStr = authHeader.ToString();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                streamToken = authStr.Substring("Bearer ".Length).Trim();
            }
        }
        
        if (string.IsNullOrEmpty(streamToken) && context.Request.Headers.TryGetValue("X-Stream-Token", out var streamTokenHeader))
        {
            streamToken = streamTokenHeader.ToString();
        }

        if (string.IsNullOrEmpty(streamToken)) return null;

        // 1. Try to find the token in the ApiKeyCollection
        var apiKeyFilter = Builders<ApiKey>.Filter.And(
            Builders<ApiKey>.Filter.Eq(a => a.Token, streamToken),
            Builders<ApiKey>.Filter.Eq(a => a.DeletedAt, null)
        );
        var apiKey = await _apiKeys.Find(apiKeyFilter).FirstOrDefaultAsync();
        if (apiKey != null)
        {
            var profileFilter = Builders<Profile>.Filter.And(
                Builders<Profile>.Filter.Eq(p => p.Id, apiKey.ProfileId),
                Builders<Profile>.Filter.Eq(p => p.DeletedAt, null)
            );
            var profile = await _profiles.Find(profileFilter).FirstOrDefaultAsync();
            if (profile != null) return profile;
        }

        // 2. Fallback to ProfileCollection StreamToken lookup (backward compatibility)
        var filter = Builders<Profile>.Filter.And(
            Builders<Profile>.Filter.Eq(p => p.StreamToken, streamToken),
            Builders<Profile>.Filter.Eq(p => p.DeletedAt, null)
        );

        return await _profiles.Find(filter).FirstOrDefaultAsync();
    }
    
    public async Task UpdateProfileActiveStateAsync(string profileId, string stationId, string episodeId)
    {
        var update = Builders<Profile>.Update
            .Set(p => p.LastActiveStationId, stationId)
            .Set(p => p.LastActiveEpisodeId, episodeId);
        
        await _profiles.UpdateOneAsync(p => p.Id == profileId, update);
    }
}
