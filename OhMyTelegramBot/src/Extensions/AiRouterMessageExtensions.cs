using OhMyLib.Services.AiRouter;
using Telegram.Bot.Extensions;

namespace OhMyTelegramBot.Extensions;

public static class AiRouterMessageExtensions
{
    public static string MdCode(string value) => $"`{value.Replace("\\", "\\\\").Replace("`", "\\`")}`";

    public static string ToMarkdownV2Message(this AiRouterSignResult result)
    {
        var status = result.Type switch
        {
            AiRouterSignResultType.Success => "签到成功",
            AiRouterSignResultType.AlreadySigned => "今日已签到",
            _ => "签到失败"
        };

        var lines = new List<string>
        {
            $"账号：{MdCode(result.Account)}",
            $"结果：{Markdown.Escape(status)}",
            $"说明：{Markdown.Escape(result.Message)}"
        };

        if (result.SignIn != null)
        {
            lines.Add($"今日奖励：{Markdown.Escape(result.SignIn.TodayReward.ToString("F2"))}");
            lines.Add($"连续签到：{Markdown.Escape(result.SignIn.CurrentStreak.ToString())} 天");
            lines.Add($"累计奖励：{Markdown.Escape(result.SignIn.TotalReward.ToString("F2"))}");
            lines.Add($"本月签到：{Markdown.Escape(result.SignIn.MonthSignedDays.ToString())} 天");
        }

        return string.Join('\n', lines);
    }
}
