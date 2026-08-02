namespace OhMyBot.Core.Infrastructure.Plugins;

public sealed class NullPluginManager : IPluginManager
{
    public IReadOnlyList<PluginRuntimeInfo> GetPlugins() => [];

    public Task<PluginReloadResult> ReloadAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PluginReloadResult(
            false,
            "插件运行时未启用。",
            []));
    }

    public Task<PluginActivationResult> DisableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PluginActivationResult(false, "插件运行时未启用。"));
    }

    public Task<PluginActivationResult> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PluginActivationResult(false, "插件运行时未启用。"));
    }
}
