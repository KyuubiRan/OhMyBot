namespace OhMyBot.Core.Host.Plugins;

public sealed class PluginRuntimeOptions
{
    public string PluginPath { get; set; } = "Plugins";

    public string ShadowPath { get; set; } = ".plugin-cache";

    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
