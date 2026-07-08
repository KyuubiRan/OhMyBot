namespace OhMyBot.Core.Infrastructure.Data.Entities;

public class SklandAccount
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    /// <summary>鹰角网络 OAuth token（用于在 cred 失效时重新获取）。</summary>
    public string HgTokenCiphertext { get; set; } = string.Empty;

    /// <summary>森空岛 cred（长期有效，每个请求的 cred header）。</summary>
    public string CredCiphertext { get; set; } = string.Empty;

    /// <summary>森空岛 sign token（短期有效，用于请求签名；过期后用 GET /web/v1/auth/refresh 刷新）。</summary>
    public string SignTokenCiphertext { get; set; } = string.Empty;

    /// <summary>森空岛 userId。</summary>
    public string SklandUserId { get; set; } = string.Empty;

    /// <summary>稳定 UUID，格式固定为不带连字符的 32 位小写 hex，dId header 值为 "B" + DeviceId。</summary>
    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    /// <summary>
    /// 手动游戏签到的游戏类型勾选（逗号分隔的 appCode）。
    /// 空串=默认全选；"-"=显式全不选；其余为选中的 appCode 列表。与自动签到相互独立。
    /// </summary>
    public string GameSignSelection { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SklandGameRole> Roles { get; set; } = new List<SklandGameRole>();
}
