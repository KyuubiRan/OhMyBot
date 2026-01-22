using OhMyLib.Dto;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Extensions;

// ReSharper disable once InconsistentNaming
public static class TUserDtoExtensions
{
    extension(TelegramUserDto userDto)
    {
        public User ToUser() => new User
        {
            Id = userDto.UserId,
            Username = userDto.Username,
            FirstName = userDto.FirstName ?? "",
            LastName = userDto.LastName
        };
    }
}