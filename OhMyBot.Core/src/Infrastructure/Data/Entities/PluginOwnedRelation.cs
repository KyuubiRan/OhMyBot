namespace OhMyBot.Core.Infrastructure.Data.Entities;

public sealed class PluginOwnedRelation
{
    public string PluginId { get; set; } = string.Empty;

    public string SchemaName { get; set; } = "public";

    public string TableName { get; set; } = string.Empty;

    public string CoreUserIdColumn { get; set; } = "CoreUserId";
}
