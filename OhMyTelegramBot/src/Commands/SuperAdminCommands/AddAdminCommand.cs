using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.SuperAdminCommands;

[Component(Key = "cmd__promote")]
public class AddAdminCommand(BotUserService service, CommandContext context, TMessageHelperService helperService) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.SuperAdmin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
        var id = mentioned?.Id.ToString() ?? args.ElementAtOrDefault(0);
        if (!long.TryParse(id, out _))
            return;

        var target = await service.GetCachedUserAsync(id, SoftwareType.Telegram);

        if (target.Uid == senderId.ToString())
        {
            await botClient.SendMessage(chatId, "喵喵喵？");
            return;
        }

        if (target.Privilege >= UserPrivilege.Owner)
        {
            await botClient.SendMessage(chatId, "休要造反！");
            return;
        }

        if (context.Privilege < target.Privilege)
        {
            await botClient.SendMessage(chatId, "无法操作比自己权限更高的用户");
            return;
        }

        if (target.Privilege >= UserPrivilege.Admin)
        {
            await botClient.SendMessage(chatId, "该用户已有权限");
            return;
        }

        await service.SetPrivilegeAsync(id, SoftwareType.Telegram, UserPrivilege.Admin);
        await botClient.SendMessage(chatId, $"已提升该用户权限至 {nameof(UserPrivilege.Admin)}");
    }
}