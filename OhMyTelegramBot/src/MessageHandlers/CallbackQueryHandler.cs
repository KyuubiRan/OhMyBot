using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OhMyLib.Attributes;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component(Key = "handler__CallbackQuery")]
public class CallbackQueryHandler(BotActionManager actionManager, IServiceProvider provider, ITelegramBotClient botClient) : ICallbackQueryHandler
{
    public async Task OnReceiveCallback(CallbackQuery query)
    {
        if (query.Data.IsWhiteSpaceOrNull)
            return;

        var data = query.Data!;
        var acton = await actionManager.GetActionAsync(data);
        if (acton == null)
            return;

        var service = provider.GetKeyedService<IBotActionHandler>("action__" + acton.ActionType);
        if (service == null)
            return;

        if (service.OnlyForOwner && query.From.Id != acton.SenderId)
            return;

        await service.OnReceiveAction(botClient, query, acton);
    }
}