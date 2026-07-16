namespace OhMyBot.Plugin.Abstractions;

public enum PluginState
{
    Discovered,
    Validating,
    Configured,
    Initialized,
    Starting,
    Active,
    Draining,
    Stopping,
    Disposed,
    Unloaded,
    Faulted,
    Disabled
}
