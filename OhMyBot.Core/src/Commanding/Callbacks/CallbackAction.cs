namespace OhMyBot.Core.Commanding.Callbacks;

public sealed record CallbackAction(
    string ActionType,
    string Hash,
    long CoreUserId,
    string ChatId,
    string SenderId,
    bool RequireOriginalSender,
    string DataJson,
    string? OwnerPluginId = null,
    int PayloadVersion = 1);
