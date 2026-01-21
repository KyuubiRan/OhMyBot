using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Commands;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component]
public sealed partial class CommandHandler(
    ILogger<CommandHandler> logger,
    ITelegramBotClient botClient,
    IServiceProvider serviceProvider,
    BotUserService userService
)
{
    public async Task HandleCommand(Message message, string command, params string[] args)
    {
        var chatId = message.Chat.Id;
        var senderId = message.From?.Id ?? 0;

        LogHandleCommand(chatId, senderId, command, args);

        var commandLower = command.ToLowerInvariant();
        var cmd = serviceProvider.GetKeyedService<ICommand>("cmd__" + commandLower);
        if (cmd != null)
        {
            var user = await userService.GetCachedUserAsync(senderId.ToString(), SoftwareType.Telegram);
            if (user.Privilege < cmd.RequirePrivilege)
            {
                LogNotEnoughPriv(senderId, command, cmd.RequirePrivilege, user.Privilege);
                return;
            }

            var context = serviceProvider.GetRequiredService<CommandContext>();
            context.ChatId = chatId;
            context.SenderId = senderId;
            context.Command = commandLower;
            context.Args = args;
            context.User = user;

            try
            {
                await cmd.OnReceiveCommand(botClient, message, chatId, senderId, args);
            }
            catch (Exception e)
            {
                LogUnhandledCommandException(e, chatId, senderId, command, args);
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "Unhandled command exception occurred! CID={chatId} (SID={senderId}), Command='{command}', Args={args}")]
    private partial void LogUnhandledCommandException(Exception e, long chatId, long senderId, string command, string[] args);

    [LoggerMessage(LogLevel.Information, "Handling command '{command}' with args {args} from CID={chatId} (SID={senderId})")]
    private partial void LogHandleCommand(long chatId, long senderId, string command, string[] args);

    [LoggerMessage(LogLevel.Information,
        "User SID={senderId} does not have enough privilege to run command '{command}' (required: {required}, actual: {actual})")]
    private partial void LogNotEnoughPriv(long senderId, string command, UserPrivilege required, UserPrivilege actual);
}