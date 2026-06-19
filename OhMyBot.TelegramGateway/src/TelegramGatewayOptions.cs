namespace OhMyBot.TelegramGateway;

public sealed class TelegramGatewayOptions
{
    public string BotInstanceId { get; set; } = "telegram-default";

    public string BotToken { get; set; } = string.Empty;

    public string CoreGrpcAddress { get; set; } = "http://localhost:5100";

    public bool DropPendingUpdates { get; set; } = true;
}
