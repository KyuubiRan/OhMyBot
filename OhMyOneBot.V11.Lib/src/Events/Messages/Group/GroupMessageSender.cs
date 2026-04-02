using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupMessageSender : Sender
{
    [JsonPropertyName("card")] public string Card { get; set; } = string.Empty;
    [JsonPropertyName("aera")] public string Aera { get; set; } = string.Empty;
    [JsonPropertyName("level")] public string Level { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    public bool IsOwner => Role == "owner";
    public bool IsAdmin => Role == "admin";
    public bool IsAdminOrOwner => IsOwner || IsAdmin;
}