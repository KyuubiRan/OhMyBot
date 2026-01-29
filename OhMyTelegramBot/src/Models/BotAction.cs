namespace OhMyTelegramBot.Models;

public record BotAction(
    string ActionType,
    string Hash,
    long ChatId,
    long SenderId
);