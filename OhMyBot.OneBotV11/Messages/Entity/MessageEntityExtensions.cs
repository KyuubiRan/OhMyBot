using OhMyBot.OneBotV11.Events.Messages;

namespace OhMyBot.OneBotV11.Messages.Entity;

public static class MessageEntityExtensions
{
    public static MessageEntity? GetMessageByType(this MessageEvent msg, MessageType type, int n = 0)
    {
        return msg.Message.GetMessageByType(type, n);
    }

    public static MessageEntity? GetMessageByType(this IEnumerable<MessageEntity> msg, MessageType type, int n = 0)
    {
        return msg.GetMessageByType(type.ToProtocolName(), n);
    }

    public static MessageEntity? GetMessageByType(this MessageEvent msg, string type, int n = 0)
    {
        return msg.Message.GetMessageByType(type, n);
    }

    public static MessageEntity? GetMessageByType(this IEnumerable<MessageEntity> msg, string type, int n = 0)
    {
        ArgumentNullException.ThrowIfNull(msg);
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "n must be zero or greater.");
        }

        var normalizedType = NormalizeType(type);
        if (normalizedType.Length == 0)
        {
            return null;
        }

        return msg
            .Where(entity => string.Equals(entity.Type, normalizedType, StringComparison.Ordinal))
            .Skip(n)
            .FirstOrDefault();
    }

    public static IEnumerable<MessageEntity> GetMessagesByType(this MessageEvent msg, MessageType type)
    {
        return msg.Message.GetMessagesByType(type);
    }

    public static IEnumerable<MessageEntity> GetMessagesByType(this IEnumerable<MessageEntity> msg, MessageType type)
    {
        return msg.GetMessagesByType(type.ToProtocolName());
    }

    public static IEnumerable<MessageEntity> GetMessagesByType(this MessageEvent msg, string type)
    {
        return msg.Message.GetMessagesByType(type);
    }

    public static IEnumerable<MessageEntity> GetMessagesByType(this IEnumerable<MessageEntity> msg, string type)
    {
        ArgumentNullException.ThrowIfNull(msg);
        var normalizedType = NormalizeType(type);
        return normalizedType.Length == 0
            ? []
            : msg.Where(entity => string.Equals(entity.Type, normalizedType, StringComparison.Ordinal));
    }

    public static string ToProtocolName(this MessageType type)
    {
        return type switch
        {
            MessageType.Text => "text",
            MessageType.Face => "face",
            MessageType.Image => "image",
            MessageType.Record => "record",
            MessageType.Video => "video",
            MessageType.At => "at",
            MessageType.Rps => "rps",
            MessageType.Dice => "dice",
            MessageType.Shake => "shake",
            MessageType.Poke => "poke",
            MessageType.Anonymous => "anonymous",
            MessageType.Share => "share",
            MessageType.Contact => "contact",
            MessageType.Location => "location",
            MessageType.Music => "music",
            MessageType.Reply => "reply",
            MessageType.Forward => "forward",
            MessageType.Node => "node",
            MessageType.Xml => "xml",
            MessageType.Json => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported OneBot message type.")
        };
    }

    private static string NormalizeType(string? type)
    {
        return string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
    }
}
