namespace OhMyBot.Plugin.Abstractions;

/// <summary>
/// Base class for a trusted, independently loaded OhMyBot plugin.
/// </summary>
public abstract class BasicPlugin : IAsyncDisposable
{
    /// <summary>
    /// Registers isolated plugin services and host-visible plugin components.
    /// This method must not start background work or perform I/O.
    /// </summary>
    public virtual void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }

    public virtual Task InitializeAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.CompletedTask;
    }

    public virtual Task StartAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.CompletedTask;
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
