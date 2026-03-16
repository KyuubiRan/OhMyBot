using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;
using OhMyLib.Attributes;
using OhMyLib.Extensions;
using OhMyTelegramBot.Models;

namespace OhMyTelegramBot.Services;

[Component(Scope = ComponentAttribute.LifetimeScope.Singleton)]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class BotActionManager(IDistributedCache cache)
{
    private static string KeyForAction(string hash) => $"bot_action:action:{hash}";
    private static string KeyForData(string hash) => $"bot_action:data:{hash}";

    public static string GenerateHash()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, 32).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    public async Task<string> PutActionAsync<T>(string type, long chatId, long senderId, T data, TimeSpan? keepTime = null,
                                                CancellationToken cancellationToken = default)
    {
        var hash = GenerateHash();

        var opts = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = keepTime ?? TimeSpan.FromMinutes(5)
        };

        await cache.SetObjectAsync(KeyForAction(hash), new BotAction(type, hash, chatId, senderId), opts, cancellationToken: cancellationToken);
        await cache.SetObjectAsync(KeyForData(hash), data, cancellationToken: cancellationToken);

        return hash;
    }

    public async Task<BotAction?> GetActionAsync(string hash, CancellationToken cancellationToken = default)
    {
        return await cache.GetObjectAsync<BotAction>(KeyForAction(hash), cancellationToken: cancellationToken);
    }

    public async Task<T?> GetActionDataAsync<T>(string hash, CancellationToken cancellationToken = default)
    {
        return await cache.GetObjectAsync<T>(KeyForData(hash), cancellationToken: cancellationToken);
    }
}