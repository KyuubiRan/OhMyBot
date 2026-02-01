using System.Diagnostics;
using System.Reflection;
using FoxTail.Extensions;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.OwnerCommands;

[Component(Key = "cmd__reboot")]
public sealed class RebootCommand(ILogger<RebootCommand> logger) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.Owner;
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty || !args[0].Equals("confirm", StringComparison.CurrentCultureIgnoreCase))
        {
            await botClient.SendMessage(
                chatId,
                "输入`/reboot confirm`来确认重启Bot\n*注：在Unix系统下无法正常工作*",
                ParseMode.MarkdownV2
            );
        }
        else
        {
            await botClient.SendMessage(
                chatId,
                "正在重启Bot..."
            );

            var processPath = Environment.ProcessPath;
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            var exe = processPath ?? entryAssembly;

            var target = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"reboot_chatid={chatId}",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });

            logger.LogInformation("Started new process with PID {Pid} for reboot", target?.Id);

            Environment.Exit(0);
        }
    }
}