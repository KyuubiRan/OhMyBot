using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class FallbackTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return true;
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        if (response.Code != 0)
        {
            return [TelegramTextMessage.PlainText($"错误：{response.Message}（{response.ErrorCode}）")];
        }

        if (response.DataKind == CommandResponseDataKind.Text)
        {
            return string.IsNullOrWhiteSpace(response.Text.Text) ? [] : [TelegramTextMessage.PlainText(response.Text.Text)];
        }

        return string.IsNullOrWhiteSpace(response.Message) ? [] : [TelegramTextMessage.PlainText(response.Message)];
    }
}
