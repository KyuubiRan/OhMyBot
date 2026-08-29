using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyBot.Contracts.Messaging;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Grpc;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Infrastructure.Linking;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Commanding.Platform;
using OhMyBot.Core.Commanding.Qq;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Core.Infrastructure.Security;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Infrastructure.UserProfiles;
using OhMyBot.Core.Infrastructure.Plugins;
using RouteOptions = OhMyBot.Core.Commanding.Routing.RouteOptions;

namespace OhMyBot.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOhMyBotCoreServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<LinkTokenOptions>().BindConfiguration("LinkToken");
        services.AddOptions<IdentityCacheOptions>().BindConfiguration("IdentityCache");
        services.AddOptions<UserProfileCacheOptions>().BindConfiguration("UserProfileCache");
        services.AddOptions<RouteOptions>().BindConfiguration("Routes");
        services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");
        services.AddOptions<EncryptionOptions>().BindConfiguration("Encryption");
        services.AddOptions<CallbackActionOptions>().BindConfiguration("CallbackActions");
        services.AddOptions<QqMenuOptions>().BindConfiguration("QqMenu");
        services.AddOptions<GrpcAccessOptions>().BindConfiguration("Grpc");
        services.AddSingleton<AccessTokenInterceptor>();
        services.TryAddSingleton<InteractiveConsoleState>();
        services.TryAddSingleton<IPluginManager, NullPluginManager>();
        services.TryAddSingleton<Func<IPluginManager>>(provider =>
            () => provider.GetRequiredService<IPluginManager>());
        services.AddScoped<IAdminCommand, UserAdminCommand>();
        services.AddScoped<IAdminCommand, TaskCtlAdminCommand>();
        services.AddScoped<IAdminCommand, PushMessageAdminCommand>();
        services.AddScoped<IAdminCommand, PluginAdminCommand>();
        services.AddSingleton<PluginAdminCommandRegistry>();
        services.AddScoped<AdminCommandCatalog>();
        services.AddScoped<AdminCommandExecutor>();
        services.AddScoped<CoreIdentityService>();
        services.AddScoped<CoreUserMergeService>();
        services.AddScoped<SetPrivilegeService>();
        services.AddScoped<PlatformUserProfileService>();
        services.AddScoped<CommandExecutionService>();
        services.AddScoped<CallbackExecutionService>();
        services.AddSingleton<IPlatformCommandDslProvider, CoreCommandDslProvider>();
        services.AddSingleton<NotificationCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider>(provider =>
            provider.GetRequiredService<NotificationCommandDslProvider>());
        services.AddScoped<ILinkTokenStore, DistributedCacheLinkTokenStore>();
        services.AddScoped<IIdentityCache, DistributedIdentityCache>();
        services.AddScoped<IUserProfileCache, DistributedUserProfileCache>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<NotificationSubscriptionService>();
        services.AddScoped<INotificationSubscriptionService>(provider =>
            provider.GetRequiredService<NotificationSubscriptionService>());
        services.AddSingleton<PluginNotificationSourceRegistry>();
        services.AddSingleton<CallbackActionStore>();
        services.AddSingleton<PluginCallbackRegistry>();
        services.AddSingleton<QqMenuStore>();
        services.AddSingleton<QqMenuConverter>();
        services.AddSingleton<PlatformRequestDispatcher>();
        services.AddSingleton<PlatformCommandDslRegistry>();
        services.AddScoped<PlatformCommandDslExecutor>();
        services.AddSingleton<RouteStore>();
        services.AddSingleton<IRouteChangePublisher, RabbitMqRouteChangePublisher>();
        services.AddSingleton<INotificationPublisher, RabbitMqNotificationPublisher>();
        // 通知、命令进度与审批决定共用同一个发布器实例（同一 exchange、同一连接）。
        services.AddSingleton<ICommandProgressPublisher>(provider =>
            (RabbitMqNotificationPublisher)provider.GetRequiredService<INotificationPublisher>());
        services.AddSingleton<IPlatformRequestDecisionPublisher>(provider =>
            (RabbitMqNotificationPublisher)provider.GetRequiredService<INotificationPublisher>());
        services.AddSingleton<ManagedTaskRegistry>();
        // 先启动队列渲染器，确保数据库 migration 或后续 hosted service 启动失败时也能立即看到日志。
        services.AddHostedService<InteractiveConsoleRendererHostedService>();
        services.AddHostedService<DatabaseMigrationHostedService>();
        services.AddHostedService<RouteStoreHostedService>();
        services.AddHostedService<ManagedTaskHostedService>();
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

        services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Core")));
        return services;
    }

    public static IServiceCollection AddOhMyBotCoreRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConfiguration = configuration.GetSection("Redis")["Configuration"];
        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            // 未配置 Redis 时降级为进程内分布式缓存，方便无 Redis 环境（如 Windows）本地运行。
            // 注意：多实例部署时各实例缓存互不共享，需配置 Redis。
            services.AddDistributedMemoryCache();
            return services;
        }

        services.AddStackExchangeRedisCache(options => options.Configuration = redisConfiguration);
        return services;
    }

}
