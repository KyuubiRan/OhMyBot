using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Core.Commanding.Notifications;

public interface IPluginNotificationSource
{
    string Type { get; }

    string DisplayName { get; }

    int Order { get; }

    /// <summary>为空时直接显示在 /notify 根页；非空时归入对应分类子页。</summary>
    NotificationCategory? Category => null;

    /// <summary>管理该通知来源所需的最低权限。</summary>
    UserPrivilege RequiredPrivilege => UserPrivilege.VerifiedUser;

    /// <summary>允许管理该通知来源的平台。</summary>
    SupportedPlatforms SupportPlatforms => SupportedPlatforms.All;

    /// <summary>配置关闭的来源不会显示，也不能通过旧回调继续操作。</summary>
    bool Enabled => true;

    Task<bool> HasEnabledTargetsAsync(
        CommandContext context,
        CancellationToken cancellationToken = default);

    Task<CommandResponse> BuildAccountPanelAsync(
        CommandContext context,
        string? editMessageId,
        CancellationToken cancellationToken = default);

    Task<CommandResponse> ToggleAsync(
        CommandContext context,
        long accountId,
        bool toggleAll,
        string editMessageId,
        CancellationToken cancellationToken = default);
}

public static class PluginNotificationSourceAccess
{
    public static bool CanManage(IPluginNotificationSource source, CommandContext context)
    {
        var platform = CommandDsl.ToSupportedPlatform(context.Request.Platform);
        return source.Enabled
            && platform is not SupportedPlatforms.None
            && source.SupportPlatforms.HasFlag(platform)
            && (int)context.Identity.Privilege >= (int)source.RequiredPrivilege;
    }
}

public sealed record NotificationTypeCallbackData(string Type);

public sealed record NotificationCategory(string Id, string DisplayName, int Order);

public sealed record NotificationCategoryCallbackData(string CategoryId);

public sealed record NotificationAccountCallbackData(string Type, long AccountId, bool ToggleAll);

public sealed record NotificationBackCallbackData;
