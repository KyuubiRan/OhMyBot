using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class UserInfoTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response is { Code: 0, DataKind: CommandResponseDataKind.UserInfo };
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        var userInfo = response.UserInfo;
        var identity = userInfo.Identities.FirstOrDefault(identity => identity.Platform == BotPlatform.Telegram)
            ?? userInfo.Identities.FirstOrDefault();

        var lines = new List<string>();
        if (identity is not null && !string.IsNullOrWhiteSpace(identity.Uid))
        {
            lines.Add($"UID: `{EscapeMarkdownCode(identity.Uid)}`");
        }

        if (identity is not null && !string.IsNullOrWhiteSpace(identity.Username))
        {
            lines.Add($"用户名: `{EscapeMarkdownCode(FormatUsername(identity.Username))}`");
        }

        if (identity is not null && !string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            lines.Add($"昵称: `{EscapeMarkdownCode(identity.DisplayName)}`");
        }

        lines.Add($"权限: `{EscapeMarkdownCode(FormatPrivilege(userInfo.Privilege))}`");
        return [TelegramTextMessage.Markdown(string.Join(Environment.NewLine, lines))];
    }

    private static string FormatPrivilege(UserPrivilege privilege)
    {
        return UserPrivilegeNames.Format(privilege);
    }

    private static string FormatUsername(string username)
    {
        var normalized = username.Trim();
        return normalized.StartsWith('@') ? normalized : $"@{normalized}";
    }

    private static string EscapeMarkdownCode(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("`", @"\`", StringComparison.Ordinal);
    }
}
