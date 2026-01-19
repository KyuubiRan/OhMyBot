using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OhMyBot.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ComponentAttribute : Attribute
{
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
    /// If set, use this key for keyed service registration.
    /// </summary>
    public object? Key { get; set; } = null;
}

public static class ComponentAttributeExtensions
{
    public static void MapComponents(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes.Where(x => x is { IsClass: true, IsAbstract: false, IsInterface: false }))
        {
            var componentAttr = type.GetCustomAttribute<ComponentAttribute>();
            if (componentAttr is null) continue;

            var implType = type.AsType();

            var serviceType = componentAttr.DerivedFrom;
            if (serviceType is null)
            {
                var interfaces = implType.GetInterfaces();
                serviceType = interfaces.Length == 1 ? interfaces[0] : implType;
            }

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