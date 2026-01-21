using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands;

[Component(Key = "cmd__help")]
public class HelpCommand(CommandContext context) : ICommand
{
    private const string UserHelpCommandText =
        """
        *\[帮助菜单\]*
        `<尖括号>` 内为 *必填* 参数，`[方括号]` 内为 _选填_ 参数。
        /help \- 显示此帮助菜单
        """;

    private const string AdminHelpCommandText =
        """
        *\[管理员\]*
        /add \<uid/mention/reply\> \- 添加用户使用bot基本功能的权限
        /del \<uid/mention/reply\> \- 删除用户使用bot基本功能的权限
        """;

    private const string SuperAdminHelpCommandText =
        """
        *\[超级管理员\]*
        /addadmin \<uid/mention/reply\> \- 调整用户为管理员
        /deladmin \<uid/mention/reply\> \- 取消用户的管理员权限
        """;

    private const string OwnerHelpCommandText =
        """
        *\[所有者\]*
        /setpriv \<uid/mention/reply\> \- 调整用户权限等级
        /broadcast \<message\> \- 向所有用户广播消息
        """;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var text = UserHelpCommandText;
        if (context.Privilege >= UserPrivilege.Admin)
            text += "\n" + AdminHelpCommandText;
        if (context.Privilege >= UserPrivilege.SuperAdmin)
            text += "\n" + SuperAdminHelpCommandText;
        if (context.Privilege >= UserPrivilege.Owner)
            text += "\n" + OwnerHelpCommandText;

        await botClient.SendMessage(chatId, text, ParseMode.MarkdownV2);
    }
}