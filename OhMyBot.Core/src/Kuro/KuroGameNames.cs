namespace OhMyBot.Core.Kuro;

public static class KuroGameNames
{
    public const long Pgr = 2;
    public const long Wuwa = 3;

    public static string Format(long gameId, string fallback = "")
    {
        return gameId switch
        {
            Pgr => "战双帕弥什",
            Wuwa => "鸣潮",
            _ => string.IsNullOrWhiteSpace(fallback) ? $"游戏 {gameId}" : fallback
        };
    }

    public static bool TryParse(string value, out long gameId)
    {
        gameId = value.Trim().ToLowerInvariant() switch
        {
            "pgr" or "2" or "战双" or "战双帕弥什" => Pgr,
            "wuwa" or "mc" or "3" or "鸣潮" => Wuwa,
            _ => 0
        };
        return gameId != 0;
    }
}
