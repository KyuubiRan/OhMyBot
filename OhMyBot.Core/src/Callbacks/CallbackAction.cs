namespace OhMyBot.Core.Callbacks;

public sealed record CallbackAction(
    string ActionType,
    string Hash,
    long CoreUserId,
    string ChatId,
    string SenderId,
    bool RequireOriginalSender,
    string DataJson);
