using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Callbacks;

public sealed class CallbackActionStore(
    IDistributedCache cache,
    IOptions<CallbackActionOptions> options)
{
    private readonly CallbackActionOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> PutAsync<T>(
        string actionType,
        long coreUserId,
        string chatId,
        string senderId,
        T data,
        bool requireOriginalSender = true,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var hash = GenerateHash();
        var action = new CallbackAction(
            actionType,
            hash,
            coreUserId,
            chatId,
            senderId,
            requireOriginalSender,
            JsonSerializer.Serialize(data, JsonOptions));

        await cache.SetAsync(GetKey(hash), JsonSerializer.SerializeToUtf8Bytes(action, JsonOptions), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? _options.EntryTtl
        }, cancellationToken);
        return hash;
    }

    public async Task<CallbackAction?> GetAsync(string hash, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(GetKey(hash), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<CallbackAction>(bytes, JsonOptions);
    }

    public static T? ReadData<T>(CallbackAction action)
    {
        return JsonSerializer.Deserialize<T>(action.DataJson, JsonOptions);
    }

    private string GetKey(string hash) => _options.CacheKeyPrefix + hash;

    private static string GenerateHash()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
