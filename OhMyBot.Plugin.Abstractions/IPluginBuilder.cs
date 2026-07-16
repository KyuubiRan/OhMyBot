using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OhMyBot.Plugin.Abstractions;

/// <summary>
/// Builds the isolated service provider and host-visible registrations of one plugin generation.
/// </summary>
public interface IPluginBuilder
{
    IServiceCollection Services { get; }

    IConfiguration Configuration { get; }

    ICollection<IPluginRegistration> Registrations { get; }
}

public static class PluginBuilderExtensions
{
    public static IPluginBuilder Export<TContract, TImplementation>(
        this IPluginBuilder builder,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Add(new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
        builder.Services.Add(new ServiceDescriptor(
            typeof(TContract),
            serviceProvider => serviceProvider.GetRequiredService<TImplementation>(),
            lifetime));
        builder.Registrations.Add(new PluginExportRegistration(
            typeof(TContract),
            typeof(TImplementation),
            lifetime));
        return builder;
    }
}
