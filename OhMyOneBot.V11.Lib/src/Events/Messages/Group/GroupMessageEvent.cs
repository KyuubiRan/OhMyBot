using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public sealed class GroupMessageEvent : MessageEvent
{
    public class AnonymousData
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = null!;
        [JsonPropertyName("flag")] public string Flag { get; init; } = null!;
    }

    [JsonPropertyName("group_id")] public long GroupId { get; init; }
    [JsonPropertyName("anonymous")] public AnonymousData? Anonymous { get; init; }
    [JsonPropertyName("sender")] public new GroupMessageSender Sender { get; init; } = null!;
}