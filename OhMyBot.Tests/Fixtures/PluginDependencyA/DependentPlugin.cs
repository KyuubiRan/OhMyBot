using OhMyBot.Plugin.Abstractions;
using OhMyBot.Tests.PluginDependencyB;
using OhMyBot.Tests.PluginDependencyLibrary;

namespace OhMyBot.Tests.PluginDependencyA;

[OhMyBotPlugin(
    "com.ohmybot.tests.dependency-a",
    "Dependency A",
    "1.0.0",
    CoreApi = "[1.0.0,2.0.0)",
    LoadPriority = 1000,
    SupportedPlatforms = PluginSupportedPlatforms.All)]
[OhMyBotDependency("com.ohmybot.tests.dependency-b", "[1.0.0,2.0.0)")]
public sealed class DependentPlugin : BasicPlugin
{
    public override Task StartAsync(
        IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        var dependency = context.HostServices.GetExport<IDependencyService>(
            "com.ohmybot.tests.dependency-b");
        if (!string.Equals(
                dependency.AssemblyName,
                typeof(IDependencyService).Assembly.GetName().Name,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dependency plugin type identity was split across load contexts.");
        }

        var sidecarValue = dependency.CreateSidecarValue();
        if (sidecarValue.GetType() != typeof(DependencySidecarValue)
            || sidecarValue.AssemblyName != typeof(DependencySidecarValue).Assembly.GetName().Name)
        {
            throw new InvalidOperationException("Dependency plugin sidecar type identity was split across load contexts.");
        }

        return Task.CompletedTask;
    }
}
