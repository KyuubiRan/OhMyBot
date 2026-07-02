using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Mihoyo;

/// <summary>
/// 米游社/HoYoLAB 游戏目录，描述每个游戏在国服(CN)和国际服(OS)下的签到参数。
/// </summary>
public sealed record MihoyoGameDef(
    string Key,
    string Name,
    string CnGameBiz,
    string? CnActId,
    bool CnUsesZzzHost,
    string? CnSignGame,
    bool CnSetActOrigin,
    string? OsActId,
    string? OsEventBase,
    string? OsSignGame)
{
    public bool SupportsCn => CnActId is not null;

    public bool SupportsOs => OsActId is not null && OsEventBase is not null;
}

public static class MihoyoGameCatalog
{
    public static readonly IReadOnlyList<MihoyoGameDef> Games =
    [
        new("genshin", "原神", "hk4e_cn", "e202311201442471", CnUsesZzzHost: false, "hk4e", CnSetActOrigin: true,
            "e202102251931481", "https://sg-hk4e-api.hoyolab.com/event/sol", OsSignGame: null),
        new("sr", "崩坏：星穹铁道", "hkrpg_cn", "e202304121516551", CnUsesZzzHost: false, CnSignGame: null, CnSetActOrigin: true,
            "e202303301540311", "https://sg-public-api.hoyolab.com/event/luna/os", OsSignGame: null),
        new("zzz", "绝区零", "nap_cn", "e202406242138391", CnUsesZzzHost: true, "zzz", CnSetActOrigin: true,
            "e202406031448091", "https://sg-act-nap-api.hoyolab.com/event/luna/zzz/os", "zzz"),
        new("honkai3", "崩坏3", "bh3_cn", "e202306201626331", CnUsesZzzHost: false, CnSignGame: null, CnSetActOrigin: false,
            "e202110291205111", "https://sg-public-api.hoyolab.com/event/mani", OsSignGame: null),
        new("themis", "未定事件簿", "nxx_cn", "e202202251749321", CnUsesZzzHost: false, CnSignGame: null, CnSetActOrigin: false,
            "e202202281857121", "https://sg-public-api.hoyolab.com/event/luna/os", OsSignGame: null),
        new("honkai2", "崩坏学园2", "bh2_cn", "e202203291431091", CnUsesZzzHost: false, CnSignGame: null, CnSetActOrigin: false,
            OsActId: null, OsEventBase: null, OsSignGame: null)
    ];

    public static IEnumerable<MihoyoGameDef> ForRegion(MihoyoRegion region)
    {
        return region == MihoyoRegion.Cn
            ? Games.Where(game => game.SupportsCn)
            : Games.Where(game => game.SupportsOs);
    }

    public static MihoyoGameDef? FindByKey(string key)
    {
        return Games.FirstOrDefault(game => string.Equals(game.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static MihoyoGameDef? FindByGameBiz(string gameBiz)
    {
        return Games.FirstOrDefault(game => string.Equals(game.CnGameBiz, gameBiz, StringComparison.OrdinalIgnoreCase));
    }

    public static string NameForGameBiz(string gameBiz)
    {
        return Games.FirstOrDefault(game => string.Equals(game.CnGameBiz, gameBiz, StringComparison.OrdinalIgnoreCase))?.Name
               ?? gameBiz;
    }

    /// <summary>解析用户输入的游戏别名为目录中的游戏。</summary>
    public static bool TryParse(string value, out MihoyoGameDef game)
    {
        var key = value.Trim().ToLowerInvariant() switch
        {
            "genshin" or "ys" or "原神" or "hk4e" => "genshin",
            "sr" or "starrail" or "hkrpg" or "星铁" or "星穹铁道" or "崩铁" => "sr",
            "zzz" or "绝区零" or "nap" => "zzz",
            "honkai3" or "bh3" or "崩3" or "崩坏3" => "honkai3",
            "themis" or "nxx" or "未定" or "未定事件簿" => "themis",
            "honkai2" or "bh2" or "崩2" or "崩坏2" or "崩坏学园2" => "honkai2",
            _ => string.Empty
        };
        var found = key.Length > 0 ? FindByKey(key) : null;
        game = found!;
        return found is not null;
    }
}
