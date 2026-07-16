namespace OhMyBot.Plugin.Abstractions;

/// <summary>
/// Stable bridge to services owned by the Core host and exports owned by dependency plugins.
/// </summary>
public interface IPluginHostServices
{
    IServiceProvider Services { get; }

    object GetExport(string pluginId, Type contractType);

    bool TryGetExport(string pluginId, Type contractType, out object? service);
}

public static class PluginHostServicesExtensions
{
    public static TContract GetExport<TContract>(this IPluginHostServices hostServices, string pluginId)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        return (TContract)hostServices.GetExport(pluginId, typeof(TContract));
    }

    public static bool TryGetExport<TContract>(
        this IPluginHostServices hostServices,
        string pluginId,
        out TContract? service)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        if (hostServices.TryGetExport(pluginId, typeof(TContract), out var value)
            && value is TContract typed)
        {
            service = typed;
            return true;
        }

        service = null;
        return false;
    }
}
