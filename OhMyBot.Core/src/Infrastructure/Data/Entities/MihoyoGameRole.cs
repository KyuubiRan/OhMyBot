namespace OhMyBot.Core.Infrastructure.Data.Entities;

public class MihoyoGameRole
{
    public long Id { get; set; }

    public long MihoyoAccountId { get; set; }

    public MihoyoAccount MihoyoAccount { get; set; } = null!;

    /// <summary>
    /// 游戏 biz，如 hk4e_cn / hkrpg_cn / nap_cn；OS 游戏使用 *_global 形式作为标识。
    /// </summary>
    public string GameBiz { get; set; } = string.Empty;

    public string GameName { get; set; } = string.Empty;

    /// <summary>
    /// 区服 ID，如 cn_gf01；OS 合成角色为空。
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// 游戏 UID；OS 合成角色为 0(签到只需 act_id)。
    /// </summary>
    public long GameUid { get; set; }

    public string Nickname { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
