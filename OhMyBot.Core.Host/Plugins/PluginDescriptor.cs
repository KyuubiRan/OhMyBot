using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Host.Plugins;

internal sealed record PluginDescriptor(
    string SourceDirectory,
    string EntryAssemblyPath,
    string EntryTypeName,
    string AssemblyName,
    PluginMetadata Metadata);

internal sealed record PluginDiscoveryFailure(
    string SourceDirectory,
    string Error);

internal sealed record PluginDiscoveryResult(
    IReadOnlyList<PluginDescriptor> Descriptors,
    IReadOnlyList<PluginDiscoveryFailure> Failures);

internal sealed record PluginValidationFailure(
    PluginDescriptor Descriptor,
    string Error);

internal sealed record PluginValidationResult(
    IReadOnlyList<PluginDescriptor> LoadOrder,
    IReadOnlyList<PluginValidationFailure> Failures);
