using System.Diagnostics;
using System.Text;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.SuperAdminCommands;

[Component(Key = "cmd__status")]
public class SystemStatusCommand : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.SuperAdmin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var statusMessage = new StringBuilder();
        using var proc = Process.GetCurrentProcess();

        var workingSet = proc.WorkingSet64 / 1024.0 / 1024;
        var managed = GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024;

        statusMessage.AppendLine($"Working Set: {workingSet:F2} MB")
                     .AppendLine($"Managed: {managed:F2} MB")
                     .AppendLine($"Run: {DateTime.Now - proc.StartTime:g}");

        await botClient.SendMessage(chatId, statusMessage.ToString());
    }
}