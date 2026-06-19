using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Private;

public class FriendAddEvent : NoticeEvent
{
    [JsonPropertyName("user_id")] public long UserId { get; init; }
}