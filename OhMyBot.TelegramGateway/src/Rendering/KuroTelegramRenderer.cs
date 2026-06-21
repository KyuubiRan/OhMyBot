using OhMyBot.Contracts.Grpc;
using Telegram.Bot.Types.Enums;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class KuroTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response.Code == 0
            && response.DataKind is CommandResponseDataKind.KuroAccountList
                or CommandResponseDataKind.KuroBindResult
                or CommandResponseDataKind.KuroBbsSignResult
                or CommandResponseDataKind.KuroGameSignResult;
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return response.DataKind switch
        {
            CommandResponseDataKind.KuroAccountList => [TelegramTextMessage.Markdown(RenderAccountList(response.KuroAccountList))],
            CommandResponseDataKind.KuroBindResult => [TelegramTextMessage.Markdown(RenderBindResult(response.KuroBindResult))],
            CommandResponseDataKind.KuroBbsSignResult => [TelegramTextMessage.Markdown(RenderBbsSignResult(response.KuroBbsSignResult))],
            CommandResponseDataKind.KuroGameSignResult => [TelegramTextMessage.Markdown(RenderGameSignResult(response.KuroGameSignResult))],
            _ => []
        };
    }

    private static string RenderAccountList(KuroAccountListData data)
    {
        if (data.Accounts.Count == 0)
        {
            return "尚未绑定库街区账号";
        }

        var lines = new List<string> { Escape("[库街区]"), "已绑定账号：" };
        foreach (var account in data.Accounts)
        {
            lines.Add($"\\- `#{account.Id}` `{Code(account.DisplayName)}` \\({account.BbsUserId}\\)：自动签到{Escape(account.AutoSignEnabled ? "开启" : "关闭")}");
            foreach (var role in account.Roles)
            {
                lines.Add($"  \\- {Escape(role.GameName)} / `{Code(role.RoleName)}`：{Escape(role.AutoSignEnabled ? "自动签到开启" : "自动签到关闭")}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string RenderBindResult(KuroBindResultData data)
    {
        var account = data.Account;
        var lines = new List<string>
        {
            Escape(data.UpdatedExisting ? "库街区账号已更新" : "库街区账号绑定成功"),
            $"账号：`#{account.Id}` `{Code(account.DisplayName)}`",
            $"UID：`{account.BbsUserId}`"
        };
        if (account.Roles.Count > 0)
        {
            lines.Add("角色：");
            lines.AddRange(account.Roles.Select(role => $"\\- {Escape(role.GameName)} / `{Code(role.RoleName)}` \\(Lv\\.{Escape(role.GameLevel)}\\)"));
        }

        return string.Join('\n', lines);
    }

    private static string RenderBbsSignResult(KuroBbsSignResultData data)
    {
        var title = data.AutoSign ? "[库街区-自动社区签到]" : "[库街区-手动社区签到]";
        var lines = new List<string>
        {
            Escape(title),
            $"账号：`#{data.Account.Id}` `{Code(data.Account.DisplayName)}`"
        };
        lines.AddRange(data.Lines.Select(Escape));
        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(data.OccurredAtUnixSeconds).ToLocalTime();
        lines.Add($"时间：{Escape(occurredAt.ToString("yyyy-MM-dd HH:mm:ss"))}");
        return string.Join('\n', lines);
    }

    private static string RenderGameSignResult(KuroGameSignResultData data)
    {
        var title = data.AutoSign ? "[库街区-自动游戏签到]" : "[库街区-手动游戏签到]";
        var lines = new List<string>
        {
            Escape(title),
            $"账号：`#{data.Account.Id}` `{Code(data.Account.DisplayName)}`"
        };
        lines.AddRange(data.Lines.Select(Escape));
        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(data.OccurredAtUnixSeconds).ToLocalTime();
        lines.Add($"时间：{Escape(occurredAt.ToString("yyyy-MM-dd HH:mm:ss"))}");
        return string.Join('\n', lines);
    }

    private static string Escape(string value) => AiRouterTelegramRenderer.Escape(value);

    private static string Code(string value) => AiRouterTelegramRenderer.Code(value);
}
