using OhMyBot.Contracts.Grpc;
using Telegram.Bot.Extensions;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class LinkTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response is { Code: 0, DataKind: CommandResponseDataKind.LinkToken or CommandResponseDataKind.LinkResult };
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        if (response.DataKind == CommandResponseDataKind.LinkToken)
        {
            var token = Markdown.Escape(response.LinkToken.Token);
            return [TelegramTextMessage.Markdown($"绑定令牌：`{token}`{Environment.NewLine}有效期：{response.LinkToken.TtlSeconds / 60:F0} 分钟")];
        }

        return [TelegramTextMessage.PlainText(response.LinkResult.Status == "already_linked" ? "账号已经绑定。" : "账号绑定成功。")];
    }
}
