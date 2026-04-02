using OhMyOneBot.V11.Lib.Messages.Entity;

namespace OhMyOneBot.V11.Lib.Messages.CQ;

public class CQCode : MessageObject<CQCode>
{
    public override string ToString()
    {
        if (Type == "text")
            return Parameters.TryGetValue("text", out var text) ? text : string.Empty;

        return CQCodeSerializer.Serialize(this);
    }

    public static implicit operator MessageEntity(CQCode code) => new() { Type = code.Type, Parameters = code.Parameters };
}