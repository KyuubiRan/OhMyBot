namespace OhMyBot.TelegramGateway;

public sealed record GatewayCommandRequest(
    string ChatId,
    string UserId,
    string MessageId,
    string Text,
    string? DisplayName = null,
    string? Username = null);
