using Telegram.Bot.Types.InlineQueryResults;

namespace OhMyTelegramBot.Interfaces.Inline;

public interface IArticleInlineQuery : IInlineQuery
{
    public string Title { get; }
    public InputMessageContent InputMessage { get; }
    public string? Description => null;
    public string? ThumbnailUrl => null;
    public int? ThumbnailHeight => null;
    public int? ThumbnailWidth => null;
}