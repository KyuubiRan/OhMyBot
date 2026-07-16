namespace OhMyBot.Plugin.Abstractions;

public sealed record PluginDependency(
    string PluginId,
    string VersionRange,
    bool Required);

public sealed record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string CoreApi,
    int LoadPriority,
    PluginSupportedPlatforms SupportedPlatforms,
    IReadOnlyList<PluginDependency> Dependencies);
