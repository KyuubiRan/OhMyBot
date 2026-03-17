using System.Diagnostics.CodeAnalysis;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.AdminCommands;

[Component(Key = "cmd__del")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class DelUserCommand(BotUserService service, CommandContext context, TMessageHelperService helperService) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.Admin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
        var id = mentioned?.Id.ToString() ?? args.ElementAtOrDefault(0);
        if (!long.TryParse(id, out _))
            return;

        var target = await service.GetCachedUserAsync(id, SoftwareType.Telegram);

        if (target.Uid == senderId.ToString())
        {
            await botClient.SendMessage(chatId, "喵喵喵？", replyParameters: message);
            return;
        }

        if (target.Privilege >= UserPrivilege.Owner)
        {
            await botClient.SendMessage(chatId, "休要造反！", replyParameters: message);
            return;
        }

        if (context.Privilege < target.Privilege)
        {
            await botClient.SendMessage(chatId, "无法操作比自己权限更高的用户", replyParameters: message);
            return;
        }

        if (target.Privilege < UserPrivilege.User)
        {
            await botClient.SendMessage(chatId, "用户无权限", replyParameters: message);
            return;
        }

        await service.SetPrivilegeAsync(id, SoftwareType.Telegram, UserPrivilege.None);
        await botClient.SendMessage(chatId, "已删除该用户权限");
    }
}