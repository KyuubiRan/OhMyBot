using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class PingTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response is { Code: 0, DataKind: CommandResponseDataKind.Ping };
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return [TelegramTextMessage.PlainText($"Pong！Core：{response.Ping.ElapsedMs}ms")];
    }
}
