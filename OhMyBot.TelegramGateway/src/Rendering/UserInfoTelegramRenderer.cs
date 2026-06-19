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
        var lines = new List<string> { "用户信息" };
        if (userInfo.HasCoreUserId)
        {
            lines.Add($"ID: {userInfo.CoreUserId}");
        }

        lines.Add($"权限：{FormatPrivilege(userInfo.Privilege)}");

        if (userInfo.Identities.Count > 0)
        {
            lines.AddRange(userInfo.Identities.Select(identity =>
            {
                var platform = FormatPlatform(identity.Platform);
                var label = string.IsNullOrWhiteSpace(identity.DisplayName) ? identity.Username : identity.DisplayName;
                return string.IsNullOrWhiteSpace(label)
                    ? $"- {platform}:{identity.Uid}"
                    : $"- {platform}:{identity.Uid} ({label})";
            }));
        }

        return [TelegramTextMessage.PlainText(string.Join(Environment.NewLine, lines))];
    }

    private static string FormatPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => "telegram",
            BotPlatform.Qq => "qq",
            _ => "unspecified"
        };
    }

    private static string FormatPrivilege(UserPrivilege privilege)
    {
        return privilege.ToString().ToLowerInvariant();
    }
}
