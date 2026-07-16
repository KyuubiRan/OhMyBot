using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Tests.PluginDependencyB;

public interface IDependencyService
{
    string AssemblyName { get; }
}

public sealed class DependencyService : IDependencyService
{
    public string AssemblyName => typeof(IDependencyService).Assembly.GetName().Name!;
}

[OhMyBotPlugin(
    "com.ohmybot.tests.dependency-b",
    "Dependency B",
    "1.0.0",
    CoreApi = "[1.0.0,2.0.0)",
    LoadPriority = 10,
    SupportedPlatforms = PluginSupportedPlatforms.All)]
public sealed class DependencyPlugin : BasicPlugin
{
    public override void Configure(IPluginBuilder builder)
    {
        builder.Export<IDependencyService, DependencyService>();
    }
}
