using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyBot.Contracts.Messaging;
using OhMyBot.Core.Admin;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Linking;
using OhMyBot.Core.Messaging;
using OhMyBot.Core.Routing;
using OhMyBot.Core.Terminal;

namespace OhMyBot.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOhMyBotCoreServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<LinkTokenOptions>(options => { });
        services.AddOptions<IdentityCacheOptions>().BindConfiguration("IdentityCache");
        services.AddOptions<OhMyBot.Core.Routing.RouteOptions>().BindConfiguration("Routes");
        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");
        services.TryAddSingleton<InteractiveConsoleState>();
        services.AddScoped<AdminCommandExecutor>();
        services.AddScoped<CoreIdentityService>();
        services.AddScoped<CommandExecutionService>();
        services.AddScoped<ILinkTokenStore, DistributedCacheLinkTokenStore>();
        services.AddScoped<IIdentityCache, DistributedIdentityCache>();
        services.AddSingleton(new CommandRegistry(CommandExecutionService.CreateBuiltInCommands()));
        services.AddSingleton<RouteStore>();
        services.AddSingleton<IRouteChangePublisher, RabbitMqRouteChangePublisher>();
        services.AddHostedService<DatabaseMigrationHostedService>();
        services.AddHostedService<RouteStoreHostedService>();
        services.AddHostedService<InteractiveConsoleRendererHostedService>();
        services.AddHostedService<InteractiveConsoleHostedService>();
        return services;
    }

    public static IServiceCollection AddOhMyBotCoreDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        }

        services.AddDbContext<OhMyBotV2DbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }

    public static IServiceCollection AddOhMyBotCoreRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConfiguration = configuration.GetSection("Redis")["Configuration"];
        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            throw new InvalidOperationException("Redis:Configuration is required.");
        }

        services.AddStackExchangeRedisCache(options => options.Configuration = redisConfiguration);
        return services;
    }
}
