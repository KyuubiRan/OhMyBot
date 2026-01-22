using OhMyLib.Models.Telegram;

namespace OhMyLib.Dto;

public record TelegramUserDto(
    long Id,
    long UserId,
    string? Username,
    string? FirstName,
    string? LastName,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public bool ExistsInDatabase => Id > 0;

    public bool SameAs(long userId, string? username, string? firstName, string? lastName) =>
        UserId == userId
        && Username == username
        && FirstName == firstName
        && LastName == lastName;
};

public static class TelegramUserExtensions
{
    extension(TelegramUser telegramUser)
    {
        public TelegramUserDto ToDto() => new(
            Id: telegramUser.Id,
            UserId: telegramUser.UserId,
            Username: telegramUser.Username,
            FirstName: telegramUser.FirstName,
            LastName: telegramUser.LastName,
            CreatedAt: telegramUser.CreatedAt,
            UpdatedAt: telegramUser.UpdatedAt
        );
    }
}