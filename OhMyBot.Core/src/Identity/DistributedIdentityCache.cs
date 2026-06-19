using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Identity;

public sealed class DistributedIdentityCache(
    IDistributedCache cache,
    IOptions<IdentityCacheOptions> options) : IIdentityCache
{
    private readonly IdentityCacheOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CachedIdentity?> GetAsync(
        BotPlatform platform,
        string platformUserId,
        CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(GetKey(platform, platformUserId), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<CachedIdentity>(bytes, JsonOptions);
    }

    public Task SetAsync(
        BotPlatform platform,
        string platformUserId,
        CachedIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
        return cache.SetAsync(GetKey(platform, platformUserId), bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.EntryTtl
        }, cancellationToken);
    }

    public Task RemoveAsync(BotPlatform platform, string platformUserId, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(GetKey(platform, platformUserId), cancellationToken);
    }

    private string GetKey(BotPlatform platform, string platformUserId)
    {
        return $"{_options.CacheKeyPrefix}{platform}:{platformUserId}";
    }
}
