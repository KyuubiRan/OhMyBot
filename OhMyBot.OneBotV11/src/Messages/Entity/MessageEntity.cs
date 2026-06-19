using System.Text.Json;
using OhMyBot.OneBotV11.Messages.CQ;

namespace OhMyBot.OneBotV11.Messages.Entity;

public class MessageEntity : MessageObject<MessageEntity>
{
    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }

    public static implicit operator CQCode(MessageEntity e) => new() { Type = e.Type, Parameters = e.Parameters };
}