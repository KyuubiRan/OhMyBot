using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Data.Entities;

public class PlatformIdentity
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public BotPlatform Platform { get; set; }

    public string PlatformUserId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Username { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
