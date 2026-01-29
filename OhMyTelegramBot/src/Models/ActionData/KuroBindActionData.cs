namespace OhMyTelegramBot.Models.ActionData;

public record KuroBindActionData(
    bool Confirm,
    long KuroUserId,
    string? KuroToken = null,
    string? KuroDevCode = null,
    string? KuroDistinctId = null,
    string? IpAddress = null
);