using System.Reflection;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Host.Plugins;

internal static partial class PluginDiscovery
{
    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();

    public static IReadOnlyList<PluginDescriptor> Discover(string pluginRoot)
        => DiscoverDetailed(pluginRoot).Descriptors;

    public static PluginDiscoveryResult DiscoverDetailed(string pluginRoot)
    {
        if (!Directory.Exists(pluginRoot))
        {
            return new PluginDiscoveryResult([], []);
        }

        var entryAssemblies = Directory
            .EnumerateDirectories(pluginRoot)
            .Select(directory => Path.Combine(directory, "Plugin.dll"))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (entryAssemblies.Length == 0)
        {
            return new PluginDiscoveryResult([], []);
        }

        var resolverPaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(GetSharedAssemblyPaths())
            .Concat(entryAssemblies)
            .Concat(entryAssemblies.SelectMany(path =>
                Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.dll", SearchOption.AllDirectories)))
            .Where(IsManagedAssembly)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var descriptors = new List<PluginDescriptor>();
        var failures = new List<PluginDiscoveryFailure>();
        foreach (var entryAssemblyPath in entryAssemblies)
        {
            try
            {
                // A separate metadata context prevents one malformed or duplicate-identity assembly
                // from poisoning discovery of otherwise independent plugins.
                using var metadataContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths));
                var assembly = metadataContext.LoadFromAssemblyPath(entryAssemblyPath);
                var pluginTypes = GetLoadableTypes(assembly)
                    .Where(type => type is { IsClass: true, IsAbstract: false })
                    .Select(type => (Type: type, Attribute: FindAttribute(type, typeof(OhMyBotPluginAttribute).FullName!)))
                    .Where(item => item.Attribute is not null)
                    .ToArray();

                if (pluginTypes.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Plugin entry assembly '{entryAssemblyPath}' must contain exactly one [OhMyBotPlugin] class.");
                }

                var (entryType, pluginAttribute) = pluginTypes[0];
                if (!DerivesFrom(entryType, typeof(BasicPlugin).FullName!))
                {
                    throw new InvalidOperationException(
                        $"Plugin entry type '{entryType.FullName}' must derive from {typeof(BasicPlugin).FullName}.");
                }

                var constructorArguments = pluginAttribute!.ConstructorArguments;
                var id = (string)constructorArguments[0].Value!;
                var name = (string)constructorArguments[1].Value!;
                var version = (string)constructorArguments[2].Value!;
                var coreApi = GetNamedArgument(pluginAttribute, nameof(OhMyBotPluginAttribute.CoreApi), "*");
                var priority = GetNamedArgument(pluginAttribute, nameof(OhMyBotPluginAttribute.LoadPriority), 0);
                var supportedPlatforms = GetNamedArgument(
                    pluginAttribute,
                    nameof(OhMyBotPluginAttribute.SupportedPlatforms),
                    PluginSupportedPlatforms.None);
                ValidateMetadata(id, version, coreApi, supportedPlatforms, entryAssemblyPath);

                var dependencies = entryType.GetCustomAttributesData()
                    .Where(attribute => attribute.AttributeType.FullName == typeof(OhMyBotDependencyAttribute).FullName)
                    .Select(attribute => new PluginDependency(
                        (string)attribute.ConstructorArguments[0].Value!,
                        (string)attribute.ConstructorArguments[1].Value!,
                        GetNamedArgument(attribute, nameof(OhMyBotDependencyAttribute.Required), true)))
                    .ToArray();
                foreach (var dependency in dependencies)
                {
                    if (!PluginIdPattern().IsMatch(dependency.PluginId))
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{id}' declares invalid dependency id '{dependency.PluginId}'.");
                    }

                    _ = VersionRange.Parse(dependency.VersionRange);
                }

                descriptors.Add(new PluginDescriptor(
                    Path.GetDirectoryName(entryAssemblyPath)!,
                    entryAssemblyPath,
                    entryType.FullName!,
                    assembly.GetName().Name ?? throw new InvalidOperationException("Plugin assembly name is missing."),
                    new PluginMetadata(id, name, version, coreApi, priority, supportedPlatforms, dependencies)));
            }
            catch (Exception exception)
            {
                failures.Add(new PluginDiscoveryFailure(
                    Path.GetDirectoryName(entryAssemblyPath)!,
                    exception.GetBaseException().Message));
            }
        }

        return new PluginDiscoveryResult(descriptors, failures);
    }

    private static IEnumerable<string> GetSharedAssemblyPaths()
    {
        return
        [
            typeof(BasicPlugin).Assembly.Location,
            typeof(OhMyBot.Core.Commanding.Commands.IPlatformCommandDslProvider).Assembly.Location,
            typeof(OhMyBot.Contracts.Grpc.CommandResponse).Assembly.Location,
            typeof(OhMyBot.Plugin.Commanding.CommandPlugin).Assembly.Location
        ];
    }

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    private static CustomAttributeData? FindAttribute(Type type, string fullName)
    {
        return type.GetCustomAttributesData().SingleOrDefault(attribute => attribute.AttributeType.FullName == fullName);
    }

    private static bool DerivesFrom(Type type, string baseTypeName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static T GetNamedArgument<T>(CustomAttributeData attribute, string name, T fallback)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.MemberName, name, StringComparison.Ordinal))
            {
                return (T)argument.TypedValue.Value!;
            }
        }

        return fallback;
    }

    private static void ValidateMetadata(
        string id,
        string version,
        string coreApi,
        PluginSupportedPlatforms supportedPlatforms,
        string path)
    {
        if (!PluginIdPattern().IsMatch(id))
        {
            throw new InvalidOperationException($"Plugin '{path}' has invalid id '{id}'.");
        }

        _ = NuGetVersion.Parse(version);
        if (coreApi != "*")
        {
            _ = VersionRange.Parse(coreApi);
        }

        if (supportedPlatforms is PluginSupportedPlatforms.None
            || (supportedPlatforms & ~PluginSupportedPlatforms.All) != 0)
        {
            throw new InvalidOperationException(
                $"Plugin '{id}' must declare one or more valid supported platforms.");
        }
    }
}
