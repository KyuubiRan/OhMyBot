using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Infrastructure.Plugins;

public sealed record PluginRuntimeInfo(
    string Id,
    string Name,
    string Version,
    int LoadPriority,
    PluginSupportedPlatforms SupportedPlatforms,
    PluginState State,
    long Generation,
    IReadOnlyList<PluginDependency> Dependencies,
    string? LastError);

public sealed record PluginReloadResult(
    bool Success,
    string Message,
    IReadOnlyList<string> ReloadedPluginIds);

public sealed record PluginActivationResult(
    bool Success,
    string Message);

public interface IPluginManager
{
    IReadOnlyList<PluginRuntimeInfo> GetPlugins();

    Task<PluginReloadResult> ReloadAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    Task<PluginActivationResult> DisableAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    Task<PluginActivationResult> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default);
}
