using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

public sealed class QQResponseRenderer
{
    public IReadOnlyList<string> Render(CommandResponse response)
    {
        if (response.Code != 0)
        {
            return [$"{response.ErrorCode}: {response.Message}"];
        }

        return response.DataKind switch
        {
            CommandResponseDataKind.Ping => [$"Pong! Core: {response.Ping.ElapsedMs}ms"],
            CommandResponseDataKind.LinkToken => [$"Link token: {response.LinkToken.Token}{Environment.NewLine}Valid for {response.LinkToken.TtlSeconds / 60:F0} minutes."],
            CommandResponseDataKind.LinkResult => [response.LinkResult.Status == "already_linked" ? "Already linked." : "Link succeeded."],
            CommandResponseDataKind.UserInfo => [RenderUserInfo(response.UserInfo)],
            CommandResponseDataKind.Text => string.IsNullOrWhiteSpace(response.Text.Text) ? [] : [response.Text.Text],
            _ => string.IsNullOrWhiteSpace(response.Message) ? [] : [response.Message]
        };
    }

    private static string RenderUserInfo(UserInfoData data)
    {
        var lines = new List<string> { "User info" };
        if (data.HasCoreUserId)
        {
            lines.Add($"Core ID: {data.CoreUserId}");
        }

        lines.Add($"Privilege: {UserPrivilegeNames.Format(data.Privilege)}");

        if (data.Identities.Count > 0)
        {
            lines.Add("Identities:");
            lines.AddRange(data.Identities.Select(identity => $"- {FormatPlatform(identity.Platform)}:{identity.Uid}"));
        }

        return string.Join(Environment.NewLine, lines);
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
}
