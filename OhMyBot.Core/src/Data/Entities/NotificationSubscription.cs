using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Data.Entities;

public class NotificationSubscription
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public string NotificationType { get; set; } = string.Empty;

    public long TargetId { get; set; }

    public int EnabledPlatforms { get; set; }

    public string? TelegramBotInstanceId { get; set; }

    public string? TelegramChatId { get; set; }

    public string? QqBotInstanceId { get; set; }

    public string? QqChatId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
