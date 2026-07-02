using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class MihoyoTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response.Code == 0
            && response.DataKind is CommandResponseDataKind.MihoyoAccountList
                or CommandResponseDataKind.MihoyoBindResult
                or CommandResponseDataKind.MihoyoBbsSignResult
                or CommandResponseDataKind.MihoyoGameSignResult;
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return response.DataKind switch
        {
            CommandResponseDataKind.MihoyoAccountList => [TelegramTextMessage.Markdown(RenderAccountList(response.MihoyoAccountList))],
            CommandResponseDataKind.MihoyoBindResult => [TelegramTextMessage.Markdown(RenderBindResult(response.MihoyoBindResult))],
            CommandResponseDataKind.MihoyoBbsSignResult => [TelegramTextMessage.Markdown(RenderSignResult(string.Empty, response.MihoyoBbsSignResult.Account, response.MihoyoBbsSignResult.Lines, response.MihoyoBbsSignResult.AutoSign, response.MihoyoBbsSignResult.OccurredAtUnixSeconds))],
            CommandResponseDataKind.MihoyoGameSignResult => [TelegramTextMessage.Markdown(RenderSignResult("游戏", response.MihoyoGameSignResult.Account, response.MihoyoGameSignResult.Lines, response.MihoyoGameSignResult.AutoSign, response.MihoyoGameSignResult.OccurredAtUnixSeconds))],
            _ => []
        };
    }

    private static string RenderAccountList(MihoyoAccountListData data)
    {
        if (data.Accounts.Count == 0)
        {
            return "尚未绑定米游社账号";
        }

        var lines = new List<string> { Escape("[米游社]"), "已绑定账号：" };
        foreach (var account in data.Accounts)
        {
            lines.Add($"\\- `#{account.Id}` `{Code(account.DisplayName)}` \\[{Escape(RegionLabel(account.Region))}\\]：自动签到{Escape(account.AutoSignEnabled ? "开启" : "关闭")}");
            foreach (var role in account.Roles)
            {
                lines.Add($"  \\- {Escape(role.GameName)}{Escape(FormatRoleSuffix(role))}：{Escape(role.AutoSignEnabled ? "自动签到开启" : "自动签到关闭")}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string RenderBindResult(MihoyoBindResultData data)
    {
        var account = data.Account;
        var lines = new List<string>
        {
            Escape(data.UpdatedExisting ? "米游社账号已更新" : "米游社账号绑定成功"),
            $"账号：`#{account.Id}` `{Code(account.DisplayName)}` \\[{Escape(RegionLabel(account.Region))}\\]",
            $"UID：`{account.Stuid}`"
        };
        if (account.Roles.Count > 0)
        {
            lines.Add("角色：");
            lines.AddRange(account.Roles.Select(role => $"\\- {Escape(role.GameName)}{Escape(FormatRoleSuffix(role))}"));
        }

        return string.Join('\n', lines);
    }

    private static string RenderSignResult(string kind, MihoyoAccountItem account, IEnumerable<string> resultLines, bool autoSign, long occurredAtUnixSeconds)
    {
        var title = $"[米游社-{(autoSign ? "自动" : "手动")}{kind}签到]";
        var lines = new List<string>
        {
            Escape(title),
            $"账号：`#{account.Id}` `{Code(account.DisplayName)}` \\[{Escape(RegionLabel(account.Region))}\\]"
        };
        lines.AddRange(resultLines.Select(Escape));
        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(occurredAtUnixSeconds).ToLocalTime();
        lines.Add($"时间：{Escape(occurredAt.ToString("yyyy-MM-dd HH:mm:ss"))}");
        return string.Join('\n', lines);
    }

    private static string FormatRoleSuffix(MihoyoGameRoleItem role)
    {
        if (role.GameUid <= 0)
        {
            return string.Empty;
        }

        var level = string.IsNullOrWhiteSpace(role.Level) ? string.Empty : $" Lv.{role.Level}";
        return $" / {role.Nickname} ({role.GameUid}){level}";
    }

    private static string RegionLabel(int region)
    {
        return region == 0 ? "国服" : "国际服";
    }

    private static string Escape(string value) => AiRouterTelegramRenderer.Escape(value);

    private static string Code(string value) => AiRouterTelegramRenderer.Code(value);
}
