using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

public sealed record GatewayCommandRequest(
    string ChatId,
    string UserId,
    string MessageId,
    string Text,
    string? DisplayName = null,
    string? Username = null,
    BotChatType ChatType = BotChatType.Unspecified,
    string? FirstName = null,
    string? LastName = null,
    string? Nickname = null,
    string? ReplyToUserId = null);
