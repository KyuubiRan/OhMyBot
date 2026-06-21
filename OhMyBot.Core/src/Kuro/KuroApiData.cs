using System.Text.Json.Serialization;

namespace OhMyBot.Core.Kuro;

public class KuroBaseResponse
{
    public int Code { get; set; }

    public string Msg { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Raw { get; set; } = string.Empty;
}

public sealed class KuroApiResponse<T> : KuroBaseResponse
{
    public T? Data { get; set; }
}

public sealed class KuroMineData
{
    public KuroMineInfo? Mine { get; set; }
}

public sealed class KuroMineInfo
{
    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public int? GoldNum { get; set; }
}

public sealed class KuroDefaultRoleData
{
    public List<KuroDefaultRoleItem> DefaultRoleList { get; set; } = [];
}

public sealed class KuroDefaultRoleItem
{
    public int GameId { get; set; }

    public string GameLevel { get; set; } = string.Empty;

    public string RoleId { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public int ActiveDay { get; set; }
}

public sealed class KuroPostListData
{
    public List<KuroPostItem> PostList { get; set; } = [];
}

public sealed class KuroPostItem
{
    public int GameId { get; set; }

    public int GameForumId { get; set; }

    public int PostType { get; set; }

    public string PostId { get; set; } = string.Empty;

    public long UserId { get; set; }
}

public sealed class KuroPostDetailData
{
}

public sealed class KuroBbsSignInData
{
}

public sealed class KuroTaskProgressData
{
    public List<KuroTaskProgressItem> DailyTask { get; set; } = [];
}

public sealed class KuroTaskProgressItem
{
    public string Remark { get; set; } = string.Empty;

    public int CompleteTimes { get; set; }

    public int NeedActionTimes { get; set; }

    public int GainGold { get; set; }

    public bool Finished { get; set; }
}

public sealed class KuroGameSignInInitData
{
    [JsonPropertyName("isSigIn")]
    public bool IsSignIn { get; set; }

    [JsonPropertyName("sigInNum")]
    public int SignInNum { get; set; }

    public List<KuroSignInGoodsConfig> SignInGoodsConfigs { get; set; } = [];
}

public sealed class KuroSignInGoodsConfig
{
    public int SerialNum { get; set; }

    public string GoodsName { get; set; } = string.Empty;

    public int GoodsNum { get; set; }
}

public sealed class KuroGameSignInResult
{
}
