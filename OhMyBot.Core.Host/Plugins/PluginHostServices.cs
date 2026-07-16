using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Host.Plugins;

internal sealed class PluginHostServices(
    IServiceProvider services,
    Func<string, PluginHandle?> findPlugin) : IPluginHostServices
{
    public IServiceProvider Services { get; } = services;

    public object GetExport(string pluginId, Type contractType)
    {
        return TryGetExport(pluginId, contractType, out var service)
            ? service!
            : throw new InvalidOperationException(
                $"Plugin '{pluginId}' does not export service '{contractType.FullName}'.");
    }

    public bool TryGetExport(string pluginId, Type contractType, out object? service)
    {
        var handle = findPlugin(pluginId);
        if (handle is null || handle.Context.State != PluginState.Active)
        {
            service = null;
            return false;
        }

        var export = handle.Registrations.OfType<PluginExportRegistration>()
            .SingleOrDefault(item => item.ContractType == contractType);
        if (export is null)
        {
            service = null;
            return false;
        }

        service = handle.ServiceProvider.GetService(contractType);
        return service is not null;
    }
}
