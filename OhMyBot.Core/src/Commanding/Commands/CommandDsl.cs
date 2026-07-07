using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commanding.Commands;

public static class CommandDsl
{
    public static string Normalize(string command)
    {
        return command.Trim().TrimStart('/').ToLowerInvariant();
    }

    public static IReadOnlyList<string> NormalizeAliases(string name, IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return [];
        }

        var normalizedName = Normalize(name);
        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Where(alias => alias.Length > 0 && !string.Equals(alias, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SupportedPlatforms ToSupportedPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => SupportedPlatforms.Telegram,
            BotPlatform.Qq => SupportedPlatforms.QQ,
            _ => SupportedPlatforms.None
        };
    }

    public static SupportedChatTypes ToSupportedChatType(BotChatType chatType)
    {
        return chatType switch
        {
            BotChatType.Private => SupportedChatTypes.Private,
            BotChatType.Group => SupportedChatTypes.Group,
            _ => SupportedChatTypes.None
        };
    }

    // 节点「有效可用会话类型」：由可达的执行点(带 Handler 的节点)聚合而来，再受本节点自身限制约束。
    // 叶子 = 自身 SupportChatTypes；纯分组 = 各子命令有效类型的并集 ∩ 自身。
    // 于是一个子命令全为私聊(或全被禁用)的父命令，其有效类型自动收敛为私聊(或 None)，
    // 群里触发父命令会命中根路由的会话类型拦截，help 列表也会把它过滤掉——父随子自动隐藏。
    public static SupportedChatTypes EffectiveChatTypes(CommandDslNode node)
    {
        if (!node.Enabled)
        {
            return SupportedChatTypes.None;
        }

        var reachable = node.Handler is not null ? node.SupportChatTypes : SupportedChatTypes.None;
        foreach (var child in node.Children)
        {
            reachable |= EffectiveChatTypes(child);
        }

        return reachable & node.SupportChatTypes;
    }
}

