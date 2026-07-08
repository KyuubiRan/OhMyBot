namespace OhMyBot.Core.Infrastructure.Data.Entities;

public class SklandGameRole
{
    public long Id { get; set; }

    public long SklandAccountId { get; set; }

    public SklandAccount SklandAccount { get; set; } = null!;

    /// <summary>游戏 ID：1=明日方舟，3=明日方舟：终末地。</summary>
    public int GameId { get; set; }

    public string AppCode { get; set; } = string.Empty;

    public string GameName { get; set; } = string.Empty;

    /// <summary>游戏 UID（字符串形式，与 Skland API 保持一致）。</summary>
    public string Uid { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    /// <summary>等级（字符串，部分游戏返回 null）。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>服务器/频道名称。</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>终末地专用：角色所在服务器 ID。</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>终末地专用：角色 ID。</summary>
    public string RoleId { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
