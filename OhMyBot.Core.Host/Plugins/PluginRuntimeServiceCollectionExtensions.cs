using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyBot.Core.Infrastructure.Plugins;

namespace OhMyBot.Core.Host.Plugins;

internal static class PluginRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddOhMyBotPluginRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PluginRuntimeOptions>()
            .Bind(configuration.GetSection("PluginRuntime"));
        services.AddSingleton<IPluginRuntimeStateStore, PluginRuntimeStateStore>();
        services.AddSingleton<PluginManager>();
        services.Replace(ServiceDescriptor.Singleton<IPluginManager>(provider =>
            provider.GetRequiredService<PluginManager>()));
        services.AddHostedService(provider => provider.GetRequiredService<PluginManager>());
        return services;
    }
}
