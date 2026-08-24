namespace OhMyBot.Tests.PluginDependencyLibrary;

public sealed class DependencySidecarValue
{
    public string AssemblyName => typeof(DependencySidecarValue).Assembly.GetName().Name!;
}
