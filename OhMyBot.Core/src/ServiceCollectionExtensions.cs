using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyBot.Contracts.Messaging;
using OhMyBot.Core.Integrations.AiRouter;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Integrations.Kuro;
using OhMyBot.Core.Infrastructure.Linking;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Integrations.Mihoyo;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Core.Infrastructure.Security;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Infrastructure.UserProfiles;
using RouteOptions = OhMyBot.Core.Commanding.Routing.RouteOptions;

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
        services.AddOptions<MihoyoOptions>().BindConfiguration("Mihoyo");
        services.AddOptions<ScheduledTaskOptions>()
            .BindConfiguration("ScheduledTasks:AiRouterAutoSign")
            .ValidateOnStart();
        services.AddOptions<ScheduledTaskOptions>("KuroAutoSign")
            .BindConfiguration("ScheduledTasks:KuroAutoSign")
            .ValidateOnStart();
        services.AddOptions<ScheduledTaskOptions>("MihoyoAutoSign")
            .BindConfiguration("ScheduledTasks:MihoyoAutoSign")
            .ValidateOnStart();
        services.TryAddSingleton<InteractiveConsoleState>();
        services.AddScoped<IAdminCommand, UserAdminCommand>();
        services.AddScoped<IAdminCommand, TaskCtlAdminCommand>();
        services.AddScoped<IAdminCommand, PushMessageAdminCommand>();
        services.AddScoped<AdminCommandCatalog>();
        services.AddScoped<AdminCommandExecutor>();
        services.AddScoped<CoreIdentityService>();
        services.AddScoped<SetPrivilegeService>();
        services.AddScoped<PlatformUserProfileService>();
        services.AddScoped<CommandExecutionService>();
        services.AddScoped<CallbackExecutionService>();
        services.AddSingleton<IPlatformCommandDslProvider, CoreCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, AiRouterCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, KuroCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, MihoyoCommandDslProvider>();
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
        services.AddScoped<MihoyoAccountService>();
        services.AddScoped<MihoyoSignService>();
        services.AddScoped<MihoyoResponseBuilder>();
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
        services.AddSingleton<IManagedTask, MihoyoAutoSignManagedTask>();
        services.AddHttpClient<AiRouterHttpClient>(client =>
        {
            client.BaseAddress = new Uri("https://ai.router.team");
        });
        services.AddHttpClient<KuroHttpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KuroOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = options.Timeout;
        });
        services.AddHttpClient<MihoyoHttpClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MihoyoOptions>>().Value;
            client.Timeout = options.Timeout;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // 米游社 bbs-api 会 gzip 压缩响应，需自动解压否则 JSON 解析失败
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            // 米游社是国服 API，必须直连；走 HTTP_PROXY 会被 bbs-api WAF 拦成 403
            UseProxy = false
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
