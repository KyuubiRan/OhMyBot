using System.ComponentModel.DataAnnotations.Schema;

namespace OhMyBot.Core.Data.Entities;

public class AiRouterAccount
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public string LoginEmail { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordCiphertext { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
