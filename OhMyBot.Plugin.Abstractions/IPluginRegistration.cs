using Microsoft.Extensions.DependencyInjection;

namespace OhMyBot.Plugin.Abstractions;

/// <summary>
/// Marker for registrations consumed by optional plugin API layers and the host runtime.
/// </summary>
public interface IPluginRegistration;

/// <summary>
/// Language-layer-neutral description of a host-managed plugin component.
/// </summary>
public interface IPluginComponentRegistration : IPluginRegistration
{
    string Kind { get; }

    Type ServiceType { get; }

    Type ImplementationType { get; }
}

public sealed record PluginExportRegistration(
    Type ContractType,
    Type ImplementationType,
    ServiceLifetime Lifetime) : IPluginRegistration;
