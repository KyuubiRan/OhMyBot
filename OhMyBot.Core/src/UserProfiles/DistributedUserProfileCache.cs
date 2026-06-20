using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.UserProfiles;

public sealed class DistributedUserProfileCache(
    IDistributedCache cache,
    IOptions<UserProfileCacheOptions> options) : IUserProfileCache
{
    private readonly UserProfileCacheOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UserProfileUpdate?> GetAsync(
        BotPlatform platform,
        string uid,
        CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(GetKey(platform, uid), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<UserProfileUpdate>(bytes, JsonOptions);
    }

    public Task SetAsync(
        UserProfileUpdate profile,
        CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions);
        return cache.SetAsync(GetKey(profile.Platform, profile.Uid), bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.EntryTtl
        }, cancellationToken);
    }

    private string GetKey(BotPlatform platform, string uid)
    {
        return $"{_options.CacheKeyPrefix}{platform}:{uid}";
    }
}
