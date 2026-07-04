using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

// QQ 端响应渲染：纯文本，无 Markdown、无按钮。与 Telegram 的富文本分开维护。
public sealed class QQResponseRenderer
{
    public IReadOnlyList<string> Render(CommandResponse response)
    {
        if (response.Code != 0)
        {
            // Core 侧已返回中文错误文案（如「此命令只能在私聊中使用。」），直接展示；缺失时回退错误码。
            var message = string.IsNullOrWhiteSpace(response.Message) ? response.ErrorCode : response.Message;
            return string.IsNullOrWhiteSpace(message) ? [] : [message];
        }

        return response.DataKind switch
        {
            CommandResponseDataKind.Ping => [$"Pong！Core 响应耗时 {response.Ping.ElapsedMs}ms"],
            CommandResponseDataKind.LinkToken => [RenderLinkToken(response.LinkToken)],
            CommandResponseDataKind.LinkResult => [response.LinkResult.Status == "already_linked" ? "该账号已经绑定过了。" : "绑定成功。"],
            CommandResponseDataKind.UserInfo => [RenderUserInfo(response.UserInfo)],
            CommandResponseDataKind.Text => string.IsNullOrWhiteSpace(response.Text.Text) ? [] : [response.Text.Text],
            _ => string.IsNullOrWhiteSpace(response.Message) ? [] : [response.Message]
        };
    }

    private static string RenderLinkToken(LinkTokenData data)
    {
        var minutes = data.TtlSeconds / 60;
        return $"绑定令牌：{data.Token}\n有效期 {minutes} 分钟。请在另一平台发送 /link {data.Token} 完成绑定。";
    }

    private static string RenderUserInfo(UserInfoData data)
    {
        // 与 Telegram 的 info 字段对齐：UID / 用户名 / 昵称 / 权限，缺失的字段省略。
        var identity = data.Identities.FirstOrDefault(item => item.Platform == BotPlatform.Qq)
            ?? data.Identities.FirstOrDefault();

        var lines = new List<string>();
        if (identity is not null && !string.IsNullOrWhiteSpace(identity.Uid))
        {
            lines.Add($"UID: {identity.Uid}");
        }

        if (identity is not null && !string.IsNullOrWhiteSpace(identity.Username))
        {
            lines.Add($"用户名: {FormatUsername(identity.Username)}");
        }

        if (identity is not null && !string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            lines.Add($"昵称: {identity.DisplayName}");
        }

        lines.Add($"权限: {UserPrivilegeNames.Format(data.Privilege)}");
        return string.Join('\n', lines);
    }

    private static string FormatUsername(string username)
    {
        var normalized = username.Trim();
        return normalized.StartsWith('@') ? normalized : $"@{normalized}";
    }
}
