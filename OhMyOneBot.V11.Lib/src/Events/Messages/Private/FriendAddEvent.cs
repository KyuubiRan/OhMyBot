using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Private;

public class FriendAddEvent : NoticeEvent
{
    [JsonPropertyName("user_id")] public long UserId { get; init; }
}