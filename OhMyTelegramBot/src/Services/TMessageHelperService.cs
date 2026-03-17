using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Services;
using OhMyTelegramBot.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Services;

[Component]

// ReSharper disable once InconsistentNaming
public class TMessageHelperService(TelegramUserService userService)
{
    public async Task<TelegramUserDto?> GetMentionedUserAsync(Message message, int index = 0, CancellationToken cancellationToken = default)
    {
        var mentionEntity = message.Entities?
                                   .Where(e => e.Type == MessageEntityType.Mention)
                                   .ElementAtOrDefault(index);

        if (mentionEntity is null)
            return null;

        var username = message.Text?.Substring(mentionEntity.Offset + 1, mentionEntity.Length - 1); // Skip '@'
        if (string.IsNullOrEmpty(username))
            return null;

        var u = await userService.GetCachedUserByUsernameAsync(username, cancellationToken);

        return u.ExistsInDatabase ? u : null;
    }

    /// <summary>
    /// Get first reply to or mentioned user in the message
    /// </summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<User?> GetReplyToOrFirstMentionedUser(Message message, CancellationToken cancellationToken = default)
    {
        if (message.ReplyToMessage?.From != null)
            return message.ReplyToMessage.From;
        if (message.GetTextMentionedUser() is { } tu)
            return tu;

        return (await GetMentionedUserAsync(message, 0, cancellationToken))?.ToUser();
    }
}