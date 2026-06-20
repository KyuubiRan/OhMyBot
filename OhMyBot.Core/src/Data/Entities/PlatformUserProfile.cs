using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Data.Entities;

public class PlatformUserProfile
{
    public long Id { get; set; }

    public BotPlatform Platform { get; set; }

    public string Uid { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Nickname { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
