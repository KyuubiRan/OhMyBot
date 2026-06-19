using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Linking;

public sealed class DistributedCacheLinkTokenStore(
    IDistributedCache cache,
    IOptions<LinkTokenOptions> options) : ILinkTokenStore
{
    private readonly LinkTokenOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task SetAsync(string token, LinkTokenPayload payload, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return cache.SetAsync(GetKey(token), bytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        }, cancellationToken);
    }

    public async Task<LinkTokenPayload?> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(GetKey(token), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<LinkTokenPayload>(bytes, JsonOptions);
    }

    public Task RemoveAsync(string token, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(GetKey(token), cancellationToken);
    }

    private string GetKey(string token) => _options.CacheKeyPrefix + token;
}
