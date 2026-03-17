using System.Diagnostics.CodeAnalysis;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces.Handlers;
using OhMyTelegramBot.Interfaces.Inline;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;

namespace OhMyTelegramBot.MessageHandlers;

[Component("handler__InlineQuery")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class InlineQueryHandler(ITelegramBotClient botClient, BotUserService userService, IServiceProvider serviceProvider) : IInlineQueryHandler
{
    private static readonly Dictionary<IInlineQuery, InlineQueryResult> Handlers = new();

    static InlineQueryHandler()
    {
        var queryTypes = typeof(IInlineQuery).Assembly.GetTypes()
                                             .Where(t => typeof(IInlineQuery).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
                                             .Select(Activator.CreateInstance)
                                             .Select(x => (IInlineQuery)x!)
                                             .ToList();

        foreach (var type in queryTypes)
        {
            if (type is IArticleInlineQuery aiq)
            {
                Handlers.Add(aiq, new InlineQueryResultArticle(aiq.Id, aiq.Title, aiq.InputMessage)
                {
                    Description = aiq.Description,
                    ThumbnailUrl = aiq.ThumbnailUrl,
                    ThumbnailHeight = aiq.ThumbnailHeight,
                    ThumbnailWidth = aiq.ThumbnailWidth,
                    ReplyMarkup = aiq.ReplyMarkup
                });
            }
        }
    }

    public async Task OnReceiveInlineQuery(InlineQuery query)
    {
        var u = await userService.GetCachedUserAsync(query.From.Id.ToString(), SoftwareType.Telegram);
        if (u.Privilege < UserPrivilege.User)
            return;

        await botClient.AnswerInlineQuery(query.Id, Handlers.Values, isPersonal: true
#if DEBUG
                                        , cacheTime: 1
#endif
        );
    }

    public async Task OnReceiveChosenInlineQuery(ChosenInlineResult query)
    {
        await (serviceProvider.GetKeyedService<IInlineChosenQueryHandler>("inline_chosen_query_handler__" + query.ResultId)?.OnReceiveChosenInlineQuery(query))
            .OrCompletedTask();
    }
}