namespace OhMyOneBot.V11.Lib.CQ;

public class CQCode
{
    public required string Type { get; init; } = string.Empty;

    public Dictionary<string, string> Parameters { get; set; } = [];

    public override string ToString()
    {
        return CQCodeSerializer.Serialize(this);
    }
}