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

    public async Task<UserProfileCacheEntry?> GetAsync(
        BotPlatform platform,
        string uid,
        CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(GetKey(platform, uid), cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UserProfileCacheEntry>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            var legacyProfile = JsonSerializer.Deserialize<UserProfileUpdate>(bytes, JsonOptions);
            return legacyProfile is null
                ? null
                : new UserProfileCacheEntry(legacyProfile, Persisted: false);
        }
    }

    public Task SetAsync(
        UserProfileCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        return cache.SetAsync(GetKey(entry.Profile.Platform, entry.Profile.Uid), bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.EntryTtl
        }, cancellationToken);
    }

    private string GetKey(BotPlatform platform, string uid)
    {
        return $"{_options.CacheKeyPrefix}{platform}:{uid}";
    }
}
