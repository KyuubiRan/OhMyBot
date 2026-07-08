using System.Text.Json.Serialization;

namespace OhMyBot.Core.Integrations.Skland;

// ---- Base response ----

public class SklandBaseResponse
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public bool IsOk => Code == 0;

    public string Raw { get; set; } = string.Empty;
}

public sealed class SklandApiResponse<T> : SklandBaseResponse
{
    public T? Data { get; set; }
}

// ---- Hypergryph auth ----

public sealed class HgGrantData
{
    public string Code { get; set; } = string.Empty;
}

// ---- Skland cred ----

public sealed class SklandCredData
{
    public string Token { get; set; } = string.Empty;

    public string Cred { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
}

// ---- Skland token refresh ----

public sealed class SklandTokenRefreshData
{
    public string Token { get; set; } = string.Empty;
}

// ---- Binding / character info ----

public sealed class SklandBindingData
{
    public List<SklandBindingAppEntry> List { get; set; } = [];
}

public sealed class SklandBindingAppEntry
{
    public string AppCode { get; set; } = string.Empty;

    public List<SklandBindingItem> BindingList { get; set; } = [];
}

public sealed class SklandBindingItem
{
    public string Uid { get; set; } = string.Empty;

    public int GameId { get; set; }

    public string GameName { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public string ChannelName { get; set; } = string.Empty;

    public int Level { get; set; }

    /// <summary>终末地专用：账号下的所有角色（每个角色有独立 roleId + serverId）。</summary>
    public List<SklandEndfieldRole> Roles { get; set; } = [];
}

public sealed class SklandEndfieldRole
{
    public string RoleId { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;

    /// <summary>终末地角色昵称（如"喵喵#4655"）。</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>终末地角色等级。</summary>
    public int Level { get; set; }
}

// ---- Player info ----

public sealed class SklandPlayerInfoData
{
    public SklandPlayerStatusData? Status { get; set; }
}

public sealed class SklandPlayerStatusData
{
    public string Uid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Level { get; set; }

    /// <summary>服务器名称，如"官服"。</summary>
    public string ChannelName { get; set; } = string.Empty;
}

// ---- Skland user profile ----

public sealed class SklandUserInfoData
{
    public SklandUserProfile? User { get; set; }
}

public sealed class SklandUserProfile
{
    public string Nickname { get; set; } = string.Empty;

    public string Avatar { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string UserId { get; set; } = string.Empty;
}

// ---- Arknights attendance ----

public sealed class SklandAttendanceData
{
    /// <summary>签到记录列表（最近数日）。</summary>
    public List<SklandAttendanceRecord> Records { get; set; } = [];

    /// <summary>今日奖励预览列表（按天序号）。</summary>
    public List<SklandAwardItem> Awards { get; set; } = [];
}

public sealed class SklandAttendanceRecord
{
    /// <summary>Unix 秒时间戳（UTC）。</summary>
    public long Timestamp { get; set; }

    public int ResourceId { get; set; }

    public string ResourceName { get; set; } = string.Empty;
}

// ---- Attendance sign result (POST) ----

public sealed class SklandAttendanceSignResult
{
    public List<SklandAwardItem> Awards { get; set; } = [];
}

public sealed class SklandAwardItem
{
    public SklandAwardResource Resource { get; set; } = new();

    public int Count { get; set; }
}

public sealed class SklandAwardResource
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

// ---- Endfield attendance ----

public sealed class SklandEndfieldAttendanceData
{
    /// <summary>今日是否已签到。</summary>
    public bool HasToday { get; set; }

    public List<SklandEndfieldAwardEntry> AwardIds { get; set; } = [];

    public Dictionary<string, SklandEndfieldResourceInfo> ResourceInfoMap { get; set; } = [];
}

public sealed class SklandEndfieldAwardEntry
{
    public string Id { get; set; } = string.Empty;
}

public sealed class SklandEndfieldResourceInfo
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

// ---- Endfield attendance sign result (POST) ----

public sealed class SklandEndfieldAttendanceSignResult
{
    public List<SklandEndfieldAwardEntry> AwardIds { get; set; } = [];

    public Dictionary<string, SklandEndfieldResourceInfo> ResourceInfoMap { get; set; } = [];
}
