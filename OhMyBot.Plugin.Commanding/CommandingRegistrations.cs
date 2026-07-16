using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Plugin.Commanding;

public enum CommandingComponentKind
{
    PlatformCommand,
    CallbackHandler,
    CommandMiddleware,
    CallbackMiddleware,
    ManagedTask,
    HostedService,
    AdminCommand,
    NotificationSource
}

/// <summary>
/// Host-readable marker emitted for every component registered through the commanding API.
/// </summary>
public sealed record CommandingComponentRegistration(
    CommandingComponentKind ComponentKind,
    Type ServiceType,
    Type ImplementationType) : IPluginComponentRegistration
{
    public string Kind => ComponentKind.ToString();
}

public static class CommandPluginBuilderExtensions
{
    public static ICommandPluginBuilder AddPlatformCommand<TProvider>(this ICommandPluginBuilder builder)
        where TProvider : class, OhMyBot.Core.Commanding.Commands.IPlatformCommandDslProvider
        => AddComponent<TProvider, OhMyBot.Core.Commanding.Commands.IPlatformCommandDslProvider>(
            builder,
            CommandingComponentKind.PlatformCommand,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddAdminCommand<TCommand>(this ICommandPluginBuilder builder)
        where TCommand : class, OhMyBot.Core.Commanding.Admin.IAdminCommand
        => AddComponent<TCommand, OhMyBot.Core.Commanding.Admin.IAdminCommand>(
            builder,
            CommandingComponentKind.AdminCommand,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddManagedTask<TTask>(this ICommandPluginBuilder builder)
        where TTask : class, OhMyBot.Core.Infrastructure.ScheduledTasks.IManagedTask
        => AddComponent<TTask, OhMyBot.Core.Infrastructure.ScheduledTasks.IManagedTask>(
            builder,
            CommandingComponentKind.ManagedTask,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddPluginHostedService<THostedService>(this ICommandPluginBuilder builder)
        where THostedService : class, IHostedService
        => AddComponent<THostedService, IHostedService>(
            builder,
            CommandingComponentKind.HostedService,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddCallbackHandler<THandler>(this ICommandPluginBuilder builder)
        where THandler : class, OhMyBot.Core.Commanding.Callbacks.IPluginCallbackHandler
        => AddComponent<THandler, OhMyBot.Core.Commanding.Callbacks.IPluginCallbackHandler>(
            builder,
            CommandingComponentKind.CallbackHandler,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddCommandMiddleware<TMiddleware>(this ICommandPluginBuilder builder)
        where TMiddleware : class, IPluginCommandMiddleware
        => AddComponent<TMiddleware, IPluginCommandMiddleware>(
            builder,
            CommandingComponentKind.CommandMiddleware,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddCallbackMiddleware<TMiddleware>(this ICommandPluginBuilder builder)
        where TMiddleware : class, IPluginCallbackMiddleware
        => AddComponent<TMiddleware, IPluginCallbackMiddleware>(
            builder,
            CommandingComponentKind.CallbackMiddleware,
            ServiceLifetime.Singleton);

    public static ICommandPluginBuilder AddNotificationSource<TSource>(this ICommandPluginBuilder builder)
        where TSource : class, OhMyBot.Core.Commanding.Notifications.IPluginNotificationSource
        => AddComponent<TSource, OhMyBot.Core.Commanding.Notifications.IPluginNotificationSource>(
            builder,
            CommandingComponentKind.NotificationSource,
            ServiceLifetime.Singleton);

    private static ICommandPluginBuilder AddComponent<TImplementation, TService>(
        ICommandPluginBuilder builder,
        CommandingComponentKind kind,
        ServiceLifetime lifetime)
        where TImplementation : class, TService
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Add(new ServiceDescriptor(
            typeof(TImplementation),
            typeof(TImplementation),
            lifetime));
        builder.Services.Add(new ServiceDescriptor(
            typeof(TService),
            serviceProvider => serviceProvider.GetRequiredService<TImplementation>(),
            lifetime));
        builder.Plugin.Registrations.Add(new CommandingComponentRegistration(
            kind,
            typeof(TService),
            typeof(TImplementation)));
        return builder;
    }
}
