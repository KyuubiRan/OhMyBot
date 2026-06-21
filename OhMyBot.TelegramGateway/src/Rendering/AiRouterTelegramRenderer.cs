using OhMyBot.Contracts.Grpc;
using Telegram.Bot.Types.Enums;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class AiRouterTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response.Code == 0
            && response.DataKind is CommandResponseDataKind.AiRouterAccountList
                or CommandResponseDataKind.AiRouterBindResult
                or CommandResponseDataKind.AiRouterSignResult;
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return response.DataKind switch
        {
            CommandResponseDataKind.AiRouterAccountList => [TelegramTextMessage.Markdown(RenderAccountList(response.AiRouterAccountList))],
            CommandResponseDataKind.AiRouterBindResult => [TelegramTextMessage.Markdown(RenderBindResult(response.AiRouterBindResult))],
            CommandResponseDataKind.AiRouterSignResult => [TelegramTextMessage.Markdown(RenderSignResult(response.AiRouterSignResult))],
            _ => []
        };
    }

    private static string RenderAccountList(AiRouterAccountListData data)
    {
        if (data.Accounts.Count == 0)
        {
            return "尚未绑定 AI Router 账号";
        }

        var lines = new List<string> { Escape("[AI Router]"), "已绑定账号：" };
        lines.AddRange(data.Accounts.Select(account =>
            $"\\- `{Code(account.DisplayName)}` \\({Code(account.LoginEmail)}\\)：自动签到{Escape(account.AutoSignEnabled ? "开启" : "关闭")}"));
        return string.Join('\n', lines);
    }

    private static string RenderBindResult(AiRouterBindResultData data)
    {
        return string.Join('\n',
            Escape("绑定成功！"),
            $"账号：`{Code(data.DisplayName)}`",
            $"邮箱：`{Code(data.LoginEmail)}`");
    }

    private static string RenderSignResult(AiRouterSignResultData data)
    {
        var title = data.AutoSign ? "[AI Router-自动签到]" : "[AI Router-手动签到]";
        var status = data.Status switch
        {
            "success" => "签到成功",
            "already_signed" => "今日已签到",
            _ => "签到失败"
        };
        var lines = new List<string>
        {
            Escape(title),
            $"账号：`{Code(data.DisplayName)}`",
            $"邮箱：`{Code(data.LoginEmail)}`",
            $"结果：{Escape(status)}",
            $"说明：{Escape(data.Message)}"
        };

        if (!string.IsNullOrWhiteSpace(data.TodayReward))
        {
            lines.Add($"今日奖励：{Escape(data.TodayReward)}");
            lines.Add($"连续签到：{data.CurrentStreak} 天");
            lines.Add($"累计奖励：{Escape(data.TotalReward)}");
            lines.Add($"本月签到：{data.MonthSignedDays} 天");
        }

        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(data.OccurredAtUnixSeconds).ToLocalTime();
        lines.Add($"时间：{Escape(occurredAt.ToString("yyyy-MM-dd HH:mm:ss"))}");
        return string.Join('\n', lines);
    }

    internal static string Escape(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("*", @"\*", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal)
            .Replace("]", @"\]", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal)
            .Replace("~", @"\~", StringComparison.Ordinal)
            .Replace("`", @"\`", StringComparison.Ordinal)
            .Replace(">", @"\>", StringComparison.Ordinal)
            .Replace("#", @"\#", StringComparison.Ordinal)
            .Replace("+", @"\+", StringComparison.Ordinal)
            .Replace("-", @"\-", StringComparison.Ordinal)
            .Replace("=", @"\=", StringComparison.Ordinal)
            .Replace("|", @"\|", StringComparison.Ordinal)
            .Replace("{", @"\{", StringComparison.Ordinal)
            .Replace("}", @"\}", StringComparison.Ordinal)
            .Replace(".", @"\.", StringComparison.Ordinal)
            .Replace("!", @"\!", StringComparison.Ordinal);
    }

    internal static string Code(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("`", @"\`", StringComparison.Ordinal);
    }
}
