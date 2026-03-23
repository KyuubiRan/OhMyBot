using OhMyLib.Enums;
using OhMyLib.Services;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Extensions;

public static class TelegramUserServiceExtensions
{
    extension(TelegramUserService service)
    {
        public Task LogUserAsync(User user, UserPrivilege privilege = UserPrivilege.None, CancellationToken cancellationToken = default) =>
            service.LogUserAsync(user.Id, user.Username, user.FirstName, user.LastName, privilege, cancellationToken);
    }
}