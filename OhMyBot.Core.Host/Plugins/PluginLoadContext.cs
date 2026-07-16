using System.Reflection;
using System.Runtime.Loader;

namespace OhMyBot.Core.Host.Plugins;

internal sealed class PluginLoadContext(
    string entryAssemblyPath,
    Func<AssemblyName, Assembly?> resolveDependency) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryAssemblyPath);
    private readonly string _baseDirectory = Path.GetDirectoryName(entryAssemblyPath)!;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var shared = Default.Assemblies.FirstOrDefault(assembly =>
            AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
        if (shared is not null && IsSharedAssembly(assemblyName.Name))
        {
            return shared;
        }

        var dependency = resolveDependency(assemblyName);
        if (dependency is not null)
        {
            return dependency;
        }

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolvedPath is not null)
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        var localPath = Path.Combine(_baseDirectory, assemblyName.Name + ".dll");
        return File.Exists(localPath) ? LoadFromAssemblyPath(localPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    private static bool IsSharedAssembly(string? name)
    {
        return name is not null && (name == "OhMyBot.Core"
            || name == "OhMyBot.Contracts"
            || name == "OhMyBot.Plugin.Abstractions"
            || name == "OhMyBot.Plugin.Commanding"
            || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal));
    }
}
