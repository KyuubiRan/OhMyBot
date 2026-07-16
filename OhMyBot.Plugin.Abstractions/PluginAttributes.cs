namespace OhMyBot.Plugin.Abstractions;

/// <summary>
/// Declares the stable identity and compatibility requirements of a plugin entry point.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OhMyBotPluginAttribute(
    string id,
    string name,
    string version) : Attribute
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Version { get; } = version;

    public string CoreApi { get; set; } = "*";

    public int LoadPriority { get; set; }

    public PluginSupportedPlatforms SupportedPlatforms { get; set; }
}

/// <summary>
/// Declares a plugin-to-plugin dependency. Version ranges use NuGet range syntax.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class OhMyBotDependencyAttribute(
    string pluginId,
    string versionRange) : Attribute
{
    public string PluginId { get; } = pluginId;

    public string VersionRange { get; } = versionRange;

    public bool Required { get; set; } = true;
}
