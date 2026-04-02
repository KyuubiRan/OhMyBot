using System.Text.Json;
using OhMyOneBot.V11.Lib.Messages.CQ;

namespace OhMyOneBot.V11.Lib.Messages.Entity;

public class MessageEntity : MessageObject<MessageEntity>
{
    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }

    public static implicit operator CQCode(MessageEntity e) => new() { Type = e.Type, Parameters = e.Parameters };
}