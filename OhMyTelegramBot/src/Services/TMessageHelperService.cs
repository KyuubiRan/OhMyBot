using OhMyLib.Attributes;
using OhMyLib.Services;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Services;

[Component]
// ReSharper disable once InconsistentNaming
public class TMessageHelperService(TelegramUserService userService)
{
    public async Task<User?> GetMentionedUserAsync(Message message, int index = 0, CancellationToken cancellationToken = default)
    {
        var mentionEntity = message.Entities?
            .Where(e => e.Type == Telegram.Bot.Types.Enums.MessageEntityType.Mention)
            .ElementAtOrDefault(index);

        if (mentionEntity is null)
            return null;

        var username = message.Text?.Substring(mentionEntity.Offset + 1, mentionEntity.Length - 1); // Skip '@'
        if (string.IsNullOrEmpty(username))
            return null;

        var u = await userService.GetCachedUserByUsernameAsync(username, cancellationToken);

        return u.ExistsInDatabase
            ? new User
            {
                Id = u.UserId,
                Username = u.Username,
                FirstName = u.FirstName ?? "",
                LastName = u.LastName
            }
            : null;
    }
}