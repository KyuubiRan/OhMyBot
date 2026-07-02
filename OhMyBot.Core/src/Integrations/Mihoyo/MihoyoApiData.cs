using System.Text.Json.Serialization;

namespace OhMyBot.Core.Integrations.Mihoyo;

public class MihoyoBaseResponse
{
    public int Retcode { get; set; }

    public string Message { get; set; } = string.Empty;

    [JsonIgnore]
    public string Raw { get; set; } = string.Empty;

    [JsonIgnore]
    public bool Ok => Retcode == 0;
}

public sealed class MihoyoApiResponse<T> : MihoyoBaseResponse
{
    public T? Data { get; set; }
}

// --- getUserGameRolesByCookie ---

public sealed class MihoyoGameRolesData
{
    public List<MihoyoGameRoleApiItem> List { get; set; } = [];
}

public sealed class MihoyoGameRoleApiItem
{
    public string GameBiz { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string GameUid { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public int Level { get; set; }

    public string RegionName { get; set; } = string.Empty;
}

// --- getCookieAccountInfoBySToken ---

public sealed class MihoyoCookieTokenData
{
    public string CookieToken { get; set; } = string.Empty;
}

// --- getUserFullInfo（米游社账号昵称）---

public sealed class MihoyoUserFullInfoData
{
    public MihoyoUserInfo UserInfo { get; set; } = new();
}

public sealed class MihoyoUserInfo
{
    public string Uid { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;
}

// --- event/luna/info ---

public sealed class MihoyoLunaInfoData
{
    public bool IsSign { get; set; }

    public int TotalSignDay { get; set; }

    public bool FirstBind { get; set; }

    public string Today { get; set; } = string.Empty;
}

// --- event/luna/home ---

public sealed class MihoyoLunaHomeData
{
    public List<MihoyoLunaAward> Awards { get; set; } = [];
}

public sealed class MihoyoLunaAward
{
    public string Name { get; set; } = string.Empty;

    public int Cnt { get; set; }
}

// --- event/luna/sign ---

public sealed class MihoyoLunaSignData
{
    /// <summary>success==1 表示需要验证码。</summary>
    public int Success { get; set; }

    public string Gt { get; set; } = string.Empty;

    public string Challenge { get; set; } = string.Empty;
}

// --- getUserMissionsState ---

public sealed class MihoyoMissionsData
{
    public int TotalPoints { get; set; }

    public int CanGetPoints { get; set; }

    public int AlreadyReceivedPoints { get; set; }

    public List<MihoyoMissionState> States { get; set; } = [];
}

public sealed class MihoyoMissionState
{
    public int MissionId { get; set; }

    public bool IsGetAward { get; set; }

    public int HappenedTimes { get; set; }
}

// --- getForumPostList ---

public sealed class MihoyoPostListData
{
    public List<MihoyoPostWrapper> List { get; set; } = [];
}

public sealed class MihoyoPostWrapper
{
    public MihoyoPostInfo Post { get; set; } = new();
}

public sealed class MihoyoPostInfo
{
    public string PostId { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
}
