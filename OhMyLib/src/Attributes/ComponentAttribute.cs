using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OhMyLib.Attributes;

[AttributeUsage(AttributeTargets.Class)]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ComponentAttribute : Attribute
{
    public ComponentAttribute()
    {
    }

    public ComponentAttribute(bool noDerived)
    {
        NoDerived = noDerived;
    }

    public ComponentAttribute(Type derivedFrom)
    {
        DerivedFrom = derivedFrom;
    }

    public ComponentAttribute(string key, LifetimeScope scope = LifetimeScope.Scoped)
    {
        Key = key;
        Scope = scope;
    }

    public enum LifetimeScope
    {
        Scoped,
        Singleton,
        Transient,
    }

    // use properties to be more idiomatic
    public LifetimeScope Scope { get; set; } = LifetimeScope.Scoped;

    /// <summary>
    /// If set, register the implementing type as this service type as well (usually an interface or base class).
    /// </summary>
    public Type? DerivedFrom { get; set; } = null;

    /// <summary>
    /// If set to true, do not register derived types (interfaces or base classes) automatically.
    /// </summary>
    public bool NoDerived { get; set; } = false;

    /// <summary>
    /// If set, use this key for keyed service registration.
    /// </summary>
    public object? Key { get; set; } = null;
}

public static class ComponentAttributeExtensions
{
    [RequiresUnreferencedCode("This API isn't safe for trimming, since it searches for types in an arbitrary assembly.")]
    public static void MapComponents(this IServiceCollection services, Assembly assembly)
    {
        var blackListInterfaces = new[]
        {
            typeof(IDisposable),
            typeof(IAsyncDisposable),
        };

        foreach (var type in assembly.DefinedTypes.Where(x => x is { IsClass: true, IsAbstract: false, IsInterface: false }))
        {
            var componentAttr = type.GetCustomAttribute<ComponentAttribute>();
            if (componentAttr is null) continue;

            var implType = type.AsType();

            var serviceType = componentAttr.DerivedFrom ?? (componentAttr.NoDerived
                                                                ? implType
                                                                : implType.GetInterfaces().FirstOrDefault(x => !blackListInterfaces.Contains(x)) ?? implType);

            if (componentAttr.Key is not null)
            {
                var key = componentAttr.Key;

                switch (componentAttr.Scope)
                {
                    case ComponentAttribute.LifetimeScope.Transient:
                        services.AddKeyedTransient(serviceType: serviceType, serviceKey: key, implementationType: implType);
                        break;
                    case ComponentAttribute.LifetimeScope.Scoped:
                        services.AddKeyedScoped(serviceType: serviceType, serviceKey: key, implementationType: implType);
                        break;
                    case ComponentAttribute.LifetimeScope.Singleton:
                        services.AddKeyedSingleton(serviceType: serviceType, serviceKey: key, implementationType: implType);
                        break;
                }
            }
            else
            {
                switch (componentAttr.Scope)
                {
                    case ComponentAttribute.LifetimeScope.Transient:
                        services.AddTransient(serviceType, implType);
                        break;
                    case ComponentAttribute.LifetimeScope.Scoped:
                        services.AddScoped(serviceType, implType);
                        break;
                    case ComponentAttribute.LifetimeScope.Singleton:
                        services.AddSingleton(serviceType, implType);
                        break;
                }
            }
        }
    }
}