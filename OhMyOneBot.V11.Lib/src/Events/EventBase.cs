using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events;

public abstract class EventBase
{
    // 	事件发生的时间戳
    [JsonPropertyName("time")] public long Timestamp { get; init; }

    // 收到事件的机器人 QQ 号
    [JsonPropertyName("self_id")] public long SelfId { get; init; }

    // 事件类型
    [JsonPropertyName("post_type")] public string PostType { get; init; } = string.Empty;
}