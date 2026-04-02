using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupFileUploadeEvent : GroupNoticeEvent
{
    public class FileData
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("busid")] public long BusId { get; init; }
    }

    [JsonPropertyName("user_id")] public long UserId { get; init; }
    [JsonPropertyName("file")] public FileData File { get; init; } = null!;
}