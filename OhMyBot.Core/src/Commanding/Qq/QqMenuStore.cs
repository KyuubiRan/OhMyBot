using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commanding.Qq;

/// <summary>
/// QQ 数字菜单的路由索引（存 Redis，5 分钟 TTL）。存的是「某条 bot 菜单消息 → 有序的选项 payload 列表」，
/// payload 本身仍是 <see cref="Callbacks.CallbackActionStore"/> 里的一次性回调 token。
/// 流程：转换器发菜单前 <see cref="PutTokenAsync"/> 暂存选项得 token 写进消息；网关发出消息拿到 message_id 后
/// <see cref="BindAsync"/> 把 token 落到「消息 id」键；用户回复序号时 <see cref="ResolveAsync"/> 取回对应 payload。
/// </summary>
public sealed class QqMenuStore(
    IDistributedCache cache,
    IOptions<QqMenuOptions> options)
{
    private readonly QqMenuOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>暂存一组选项 payload，返回随机 token（写进 QqMessage.menu_token）。</summary>
    public async Task<string> PutTokenAsync(IReadOnlyList<string> payloads, CancellationToken cancellationToken = default)
    {
        var token = GenerateToken();
        await SetAsync(TokenKey(token), payloads, cancellationToken);
        return token;
    }

    /// <summary>网关发出菜单消息后调用：把 token 暂存的选项落到「消息 id」键；私聊再写一条「最近菜单」指针。</summary>
    public async Task<bool> BindAsync(
        string chatId,
        string messageId,
        string senderId,
        BotChatType chatType,
        string token,
        CancellationToken cancellationToken = default)
    {
        var payloads = await GetAsync(TokenKey(token), cancellationToken);
        if (payloads is null)
        {
            return false;
        }

        await SetAsync(MessageKey(chatId, messageId), payloads, cancellationToken);
        if (chatType == BotChatType.Private && !string.IsNullOrEmpty(senderId))
        {
            await SetAsync(LatestKey(chatId, senderId), [messageId], cancellationToken);
        }

        await cache.RemoveAsync(TokenKey(token), cancellationToken);
        return true;
    }

    /// <summary>
    /// 按被回复的消息 id（群聊必填；私聊裸数字可空，走 latest 指针）解析出第 <paramref name="index"/> 个选项 payload。
    /// 找不到 / 越界返回 null。不消费菜单（同一菜单可依次选多项，payload 的一次性由 CallbackActionStore 负责）。
    /// </summary>
    public async Task<string?> ResolveAsync(
        string chatId,
        string? replyToMessageId,
        string senderId,
        BotChatType chatType,
        int index,
        CancellationToken cancellationToken = default)
    {
        if (index < 0)
        {
            return null;
        }

        var messageId = replyToMessageId;
        if (string.IsNullOrEmpty(messageId))
        {
            if (chatType != BotChatType.Private || string.IsNullOrEmpty(senderId))
            {
                return null;
            }

            var pointer = await GetAsync(LatestKey(chatId, senderId), cancellationToken);
            messageId = pointer?.FirstOrDefault();
            if (string.IsNullOrEmpty(messageId))
            {
                return null;
            }
        }

        var payloads = await GetAsync(MessageKey(chatId, messageId), cancellationToken);
        return payloads is not null && index < payloads.Count ? payloads[index] : null;
    }

    private async Task SetAsync(string key, IReadOnlyList<string> payloads, CancellationToken cancellationToken)
    {
        await cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(payloads, JsonOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _options.EntryTtl },
            cancellationToken);
    }

    private async Task<List<string>?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<List<string>>(bytes, JsonOptions);
    }

    private string TokenKey(string token) => $"{_options.CacheKeyPrefix}token:{token}";

    private string MessageKey(string chatId, string messageId) => $"{_options.CacheKeyPrefix}msg:{chatId}:{messageId}";

    private string LatestKey(string chatId, string senderId) => $"{_options.CacheKeyPrefix}latest:{chatId}:{senderId}";

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
