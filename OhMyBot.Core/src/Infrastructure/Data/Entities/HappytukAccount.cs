namespace OhMyBot.Core.Infrastructure.Data.Entities;

public class HappytukAccount
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public string LoginAccount { get; set; } = string.Empty;

    public string PasswordCiphertext { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
