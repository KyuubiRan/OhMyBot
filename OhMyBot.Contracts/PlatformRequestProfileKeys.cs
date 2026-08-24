namespace OhMyBot.Contracts;

/// <summary>
/// <c>PlatformRequestReport.requester_profile</c> 的键约定。网关按平台能力尽力填充，
/// 插件按需读取；缺字段就是查不到，不是错误。定义放 Contracts 是为了让两侧共用同一份键，
/// 而不是各自硬编码字符串。
/// </summary>
public static class PlatformRequestProfileKeys
{
    /// <summary>申请人昵称。</summary>
    public const string Nickname = "nickname";

    /// <summary>性别原始值（OneBot 为 male / female / unknown）。</summary>
    public const string Gender = "gender";

    /// <summary>年龄。</summary>
    public const string Age = "age";

    /// <summary>平台等级（QQ 等级）。</summary>
    public const string Level = "level";

    /// <summary>头像 URL。</summary>
    public const string AvatarUrl = "avatar_url";

    /// <summary>
    /// 请求涉及的群名（邀请入群 / 入群申请）。它描述的是群而不是申请人，
    /// 之所以挤在同一张 map 里，是为了不给单个展示字段再开一条协议通道。
    /// </summary>
    public const string GroupName = "group_name";
}
