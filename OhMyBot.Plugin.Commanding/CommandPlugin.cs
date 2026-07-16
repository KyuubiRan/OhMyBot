using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Plugin.Commanding;

/// <summary>
/// Convenience base class for the common case of a plugin contributing commands and related components.
/// </summary>
public abstract class CommandPlugin : BasicPlugin
{
    public sealed override void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureServices(builder.Services);
        ConfigureCommanding(new CommandPluginBuilder(builder));
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    protected abstract void ConfigureCommanding(ICommandPluginBuilder builder);
}
