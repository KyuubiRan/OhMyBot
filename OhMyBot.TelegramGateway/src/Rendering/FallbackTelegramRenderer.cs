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
            if (string.IsNullOrWhiteSpace(response.Text.Text))
            {
                return [];
            }

            return response.ButtonRows.Count > 0
                ? [TelegramTextMessage.Markdown(RenderMarkdownText(response.Text.Text))]
                : [TelegramTextMessage.PlainText(response.Text.Text)];
        }

        return string.IsNullOrWhiteSpace(response.Message) ? [] : [TelegramTextMessage.PlainText(response.Message)];
    }

    private static string RenderMarkdownText(string text)
    {
        var parts = text.Split('`');
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = index % 2 == 0
                ? AiRouterTelegramRenderer.Escape(parts[index])
                : $"`{AiRouterTelegramRenderer.Code(parts[index])}`";
        }

        return string.Concat(parts);
    }
}
