using FoxTail.Extensions;
using Microsoft.EntityFrameworkCore;
using OhMyLib;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.OwnerCommands;

[Component(Key = "cmd__sql")]
public class ExecuteRawSqlCommand(OhMyDbContext dbContext) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.Owner;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        try
        {
            var text = message.Text;
            if (text.IsWhiteSpaceOrNull)
                return;

            var sql = text[text.IndexOf(' ')..].Trim();
            if (sql.IsWhiteSpaceOrNull)
                return;
            
            if (!sql.EndsWith(';'))
                sql += ";";
            
            var result = await dbContext.Database.ExecuteSqlRawAsync(sql);
            await botClient.SendMessage(chatId, $"Update {result} row(s)", replyParameters: message);
        }
        catch (Exception e)
        {
            await botClient.SendMessage(chatId, $"执行SQL失败：{e.Message}", replyParameters: message);
        }
    }
}