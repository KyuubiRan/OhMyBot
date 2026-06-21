namespace OhMyBot.Core.Data.Entities;

public class KuroGameRole
{
    public long Id { get; set; }

    public long KuroAccountId { get; set; }

    public KuroAccount KuroAccount { get; set; } = null!;

    public long GameId { get; set; }

    public string GameName { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public long RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string GameLevel { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
