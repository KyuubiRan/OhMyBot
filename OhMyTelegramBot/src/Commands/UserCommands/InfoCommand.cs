using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__info")]
public sealed class InfoCommand(TelegramUserService tUserService, BotUserService bUserService, TMessageHelperService helperService) : ICommand
{
    private static string UserToText(User user, BotUserDto botUserDto)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ID: `{user.Id}`");
        var nickname = $"{Markdown.Escape(user.FirstName)} {Markdown.Escape(user.LastName)}".Trim();
        sb.AppendLine($"昵称: `{nickname}`");
        if (!user.Username.IsWhiteSpaceOrNull)
            sb.AppendLine($"用户名: {Markdown.Escape("@" + user.Username)}");
        sb.AppendLine($"权限: `{botUserDto.Privilege}`");
        sb.AppendLine($"哈狐币: {botUserDto.Coin}");
        return sb.ToString();
    }

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        string m;
        if (args.IsEmpty && message.ReplyToMessage == null)
        {
            var self = await tUserService.GetCachedUserByIdAsync(senderId);
            var selfBotUser = await bUserService.GetCachedUserAsync(senderId.ToString(), SoftwareType.Telegram);
            m = UserToText(self.ToUser(), selfBotUser);
        }
        else if (args.Length == 1 && long.TryParse(args[0], out var id))
        {
            var target = await tUserService.GetCachedUserByIdAsync(id);
            var targetBotUser = await bUserService.GetCachedUserAsync(id.ToString(), SoftwareType.Telegram);
            m = UserToText(target.ToUser(), targetBotUser);
        }
        else
        {
            var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
            if (mentioned == null)
            {
                await botClient.SendMessage(chatId, "未找到指定用户", replyParameters: message);
                return;
            }

            var target = await tUserService.GetCachedUserByIdAsync(mentioned.Id);
            var targetBotUser = await bUserService.GetCachedUserAsync(mentioned.Id.ToString(), SoftwareType.Telegram);
            m = UserToText(target.ToUser(), targetBotUser);
        }

        await botClient.SendMessage(chatId, m, ParseMode.MarkdownV2, replyParameters: message);
    }
}