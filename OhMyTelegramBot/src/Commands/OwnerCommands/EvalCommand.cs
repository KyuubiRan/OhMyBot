using FoxTail.Extensions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using OhMyLib;
using OhMyLib.Attributes;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.OwnerCommands;

[Component("cmd__eval")]
public class EvalCommand : ICommand
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
                                                                 .AddImports(
                                                                     "System",
                                                                     "System.Linq",
                                                                     "System.Collections.Generic",
                                                                     "System.Threading.Tasks"
                                                                 )
                                                                 .AddReferences(typeof(OhMyDbContext).Assembly,
                                                                                typeof(Application).Assembly);

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        try
        {
            var code = message.Text;
            if (code.IsWhiteSpaceOrNull)
                return;

            var codeEntity = message.Entities?.FirstOrDefault(x => x.Type == MessageEntityType.Code);
            code = codeEntity != null ? code[codeEntity.Offset..(codeEntity.Offset + codeEntity.Length)].Trim() : code[(code.IndexOf(' ') + 1)..].Trim();

            if (code.IsWhiteSpaceOrNull)
                return;

            var result = await CSharpScript.EvaluateAsync(code, Options);
            await botClient.SendMessage(chatId, $"{result}", replyParameters: message);
        }
        catch (Exception e)
        {
            await botClient.SendMessage(chatId, $"执行代码失败：{e.Message}", replyParameters: message);
        }
    }
}