using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Private;

public sealed class PrivateMessageEvent : MessageEvent
{
    [JsonPropertyName("sender")] public new PrivateMessageSender Sender { get; init; } = null!;
}