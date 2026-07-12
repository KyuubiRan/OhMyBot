using System.Text.Json.Nodes;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ScheduledTaskOptions
{
    public bool Enabled { get; set; } = true;

    public string Cron { get; set; } = "10 0 * * *";

    public JsonObject Args { get; set; } = [];

    public static void Bind(ScheduledTaskOptions options, IConfigurationSection section)
    {
        options.Enabled = section.GetValue(nameof(Enabled), options.Enabled);
        options.Cron = section[nameof(Cron)] ?? options.Cron;
        options.Args = ToJsonObject(section.GetSection(nameof(Args)));
    }

    private static JsonObject ToJsonObject(IConfigurationSection section)
    {
        var result = new JsonObject();
        foreach (var child in section.GetChildren())
        {
            result[child.Key] = ToJsonNode(child);
        }

        return result;
    }

    private static JsonNode? ToJsonNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return JsonValue.Create(section.Value);
        }

        if (children.Select(child => child.Key).SequenceEqual(Enumerable.Range(0, children.Length).Select(index => index.ToString())))
        {
            return new JsonArray(children.Select(ToJsonNode).ToArray());
        }

        var result = new JsonObject();
        foreach (var child in children)
        {
            result[child.Key] = ToJsonNode(child);
        }

        return result;
    }
}
