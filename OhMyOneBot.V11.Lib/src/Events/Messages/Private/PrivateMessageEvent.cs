using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Private;

public sealed class PrivateMessageEvent : MessageEvent
{
    [JsonPropertyName("sender")] public new PrivateMessageSender Sender { get; init; } = null!;
}