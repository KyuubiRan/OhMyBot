using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__help")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class HelpCommand(CommandContext context) : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    private const string UserHelpCommandText =
        """
        *\[帮助菜单\]*
        `<尖括号>` 内为 *必填* 参数，`[方括号]` 内为 _选填_ 参数。
        /help \[类型\] \- 显示帮助菜单，支持的类型：`kuro`
        /info \[uid/mention/reply\] \- 查找记录的用户信息
        /ping \- 测试机器人响应时间
        /imgcvt \- 图片格式转换，支持 jpg、png、webp 互转及生成贴纸
        """;

    private const string AdminHelpCommandText =
        """
        /add \<uid/mention/reply\> \- 添加用户使用bot基本功能的权限
        /del \<uid/mention/reply\> \- 删除用户使用bot基本功能的权限
        """;

    private const string SuperAdminHelpCommandText =
        """
        /promote \<uid/mention/reply\> \- 调整用户为管理员
        /demote \<uid/mention/reply\> \- 取消用户的管理员权限
        /status \- 查看内存占用信息
        """;

    private const string OwnerHelpCommandText =
        """
        /shutdown \- 关闭Bot
        /gc \- 强制触发垃圾回收
        """;

    private static readonly FrozenDictionary<string, string> SubCommandHelpTexts = new Dictionary<string, string>
    {
        {
            "kuro", """
                    *\[库街区\]*
                    /kuro\_bind \- 绑定库街区账号
                    /kuro\_signin \[类型1\] \[类型2\] \.\.\. \- 执行库街区每日签到任务，类型可选：signin、like、share、view，留空为执行全部
                    /kuro\_auto\_signin \- 设置库街区自动签到功能
                    /kuro\_game\_init\_char \- 初始化库街区游戏角色信息 用于游戏签到
                    /kuro\_game\_signin \[类型1\] \[类型2\] \.\.\. \- 执行库街区游戏签到任务，类型可选：wuwa、pgr，留空为执行全部
                    /kuro\_game\_auto\_signin \<on/off\> \[类型\]  \- 开关库街区游戏自动签到功能，类型可选：wuwa、pgr，留空为全部
                    """
        }
    }.ToFrozenDictionary();

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty)
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
        else if (SubCommandHelpTexts.TryGetValue(args[0], out var text))
        {
            await botClient.SendMessage(chatId, text, ParseMode.MarkdownV2);
        }
        else
        {
            await botClient.SendMessage(chatId, $"未知的参数 '{args[0]}'，请使用 /help 查看可用的帮助。");
        }
    }
}