using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public interface ITelegramCommandResultRenderer
{
    bool CanRender(CommandResponse response);

    IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response);
}
