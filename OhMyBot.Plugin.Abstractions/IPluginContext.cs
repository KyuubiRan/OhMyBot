using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OhMyBot.Plugin.Abstractions;

public interface IPluginContext
{
    PluginMetadata Metadata { get; }

    string PluginDirectory { get; }

    string ShadowDirectory { get; }

    long Generation { get; }

    PluginState State { get; }

    IConfiguration Configuration { get; }

    IPluginHostServices HostServices { get; }

    ILoggerFactory LoggerFactory { get; }

    TimeProvider TimeProvider { get; }

    CancellationToken LifetimeToken { get; }
}
