using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyBot.Contracts.Messaging;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Admin;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Kuro;
using OhMyBot.Core.Linking;
using OhMyBot.Core.Messaging;
using OhMyBot.Core.Notifications;
using OhMyBot.Core.Routing;
using OhMyBot.Core.ScheduledTasks;
using OhMyBot.Core.Security;
using OhMyBot.Core.Terminal;
using OhMyBot.Core.UserProfiles;
using RouteOptions = OhMyBot.Core.Routing.RouteOptions;

namespace OhMyBot.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOhMyBotCoreServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<LinkTokenOptions>(options => { });
        services.AddOptions<IdentityCacheOptions>().BindConfiguration("IdentityCache");
        services.AddOptions<UserProfileCacheOptions>().BindConfiguration("UserProfileCache");
        services.AddOptions<RouteOptions>().BindConfiguration("Routes");
        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");
        services.AddOptions<EncryptionOptions>().BindConfiguration("Encryption");
        services.AddOptions<CallbackActionOptions>().BindConfiguration("CallbackActions");
        services.AddOptions<AiRouterOptions>().BindConfiguration("AiRouter");
        services.AddOptions<KuroOptions>().BindConfiguration("Kuro");
        services.AddOptions<ScheduledTaskOptions>()
            .BindConfiguration("ScheduledTasks:AiRouterAutoSign")
            .ValidateOnStart();
        services.AddOptions<ScheduledTaskOptions>("KuroAutoSign")
            .BindConfiguration("ScheduledTasks:KuroAutoSign")
            .ValidateOnStart();
        services.TryAddSingleton<InteractiveConsoleState>();
        services.AddScoped<IAdminCommand, UserAdminCommand>();
        services.AddScoped<IAdminCommand, TaskCtlAdminCommand>();
        services.AddScoped<AdminCommandCatalog>();
        services.AddScoped<AdminCommandExecutor>();
        services.AddScoped<CoreIdentityService>();
        services.AddScoped<PlatformUserProfileService>();
        services.AddScoped<CommandExecutionService>();
        services.AddScoped<CallbackExecutionService>();
        services.AddSingleton<IPlatformCommandDslProvider, CoreCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, AiRouterCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, KuroCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, NotificationCommandDslProvider>();
        services.AddScoped<ILinkTokenStore, DistributedCacheLinkTokenStore>();
        services.AddScoped<IIdentityCache, DistributedIdentityCache>();
        services.AddScoped<IUserProfileCache, DistributedUserProfileCache>();
        services.AddScoped<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<AiRouterAccountService>();
        services.AddScoped<AiRouterSignService>();
        services.AddScoped<AiRouterResponseBuilder>();
        services.AddScoped<KuroAccountService>();
        services.AddScoped<KuroSignService>();
        services.AddScoped<KuroResponseBuilder>();
        services.AddScoped<NotificationSubscriptionService>();
        services.AddSingleton<CallbackActionStore>();
        services.AddSingleton<PlatformCommandDslRegistry>();
        services.AddScoped<PlatformCommandDslExecutor>();
        services.AddSingleton<RouteStore>();
        services.AddSingleton<IRouteChangePublisher, RabbitMqRouteChangePublisher>();
        services.AddSingleton<INotificationPublisher, RabbitMqNotificationPublisher>();
        services.AddSingleton<ManagedTaskRegistry>();
        services.AddSingleton<IManagedTask, AiRouterAutoSignManagedTask>();
        services.AddSingleton<IManagedTask, KuroAutoSignManagedTask>();
        services.AddHttpClient<AiRouterHttpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiRouterOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddHttpClient<KuroHttpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KuroOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = options.Timeout;
        });
        services.AddHostedService<DatabaseMigrationHostedService>();
        services.AddHostedService<RouteStoreHostedService>();
        services.AddHostedService<ManagedTaskHostedService>();
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
