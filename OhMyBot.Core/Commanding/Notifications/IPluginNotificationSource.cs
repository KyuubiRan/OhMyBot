using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Core.Commanding.Notifications;

public interface IPluginNotificationSource
{
    string Type { get; }

    string DisplayName { get; }

    int Order { get; }

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

public sealed record NotificationTypeCallbackData(string Type);

public sealed record NotificationAccountCallbackData(string Type, long AccountId, bool ToggleAll);

public sealed record NotificationBackCallbackData;
