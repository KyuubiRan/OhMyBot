namespace OhMyBot.Core.Integrations.Skland;

public static class SklandGameNames
{
    public const int Arknights = 1;
    public const int Endfield = 3;

    public const string ArknightsAppCode = "arknights";
    public const string EndfieldAppCode = "endfield";

    /// <summary>受支持的游戏类型，按展示顺序排列。</summary>
    public static readonly int[] Order = [Arknights, Endfield];

    public static string ToAppCode(int gameId)
    {
        return gameId switch
        {
            Arknights => ArknightsAppCode,
            Endfield => EndfieldAppCode,
            _ => gameId.ToString()
        };
    }

    public static string Format(int gameId)
    {
        return gameId switch
        {
            Arknights => "明日方舟",
            Endfield => "明日方舟：终末地",
            _ => $"游戏 {gameId}"
        };
    }

    public static bool TryParse(string value, out int gameId)
    {
        gameId = value.Trim().ToLowerInvariant() switch
        {
            "arknights" or "ak" or "方舟" or "明日方舟" or "1" => Arknights,
            "endfield" or "ef" or "终末地" or "3" => Endfield,
            _ => 0
        };
        return gameId != 0;
    }

    public static int FromAppCode(string appCode)
    {
        return appCode.ToLowerInvariant() switch
        {
            ArknightsAppCode => Arknights,
            EndfieldAppCode => Endfield,
            _ => 0
        };
    }
}
