using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Plugin.Abstractions;
using OhMyBot.Plugin.Commanding;

namespace OhMyBot.Core.Host.Plugins;

internal sealed class LeasedCommandProvider(
    string pluginId,
    PluginSupportedPlatforms supportedPlatforms,
    IPlatformCommandDslProvider inner,
    PluginInvocationGate gate,
    IReadOnlyList<IPluginCommandMiddleware> middleware) : IPlatformCommandDslProvider
{
    public IEnumerable<CommandDslNode> GetNodes() => inner.GetNodes().Select(Wrap);

    private CommandDslNode Wrap(CommandDslNode node)
    {
        return new CommandDslNode
        {
            Name = node.Name,
            Description = node.Description,
            Usage = node.Usage,
            Aliases = node.Aliases,
            RequiredPrivilege = node.RequiredPrivilege,
            SupportPlatforms = node.SupportPlatforms & supportedPlatforms.ToCommandPlatforms(),
            SupportChatTypes = node.SupportChatTypes,
            Enabled = node.Enabled,
            Handler = node.Handler is null ? null : Wrap(node.Handler),
            Children = node.Children.Select(Wrap).ToArray()
        };
    }

    private CommandDslHandler Wrap(CommandDslHandler handler)
    {
        PluginCommandHandlerDelegate pipeline = context => handler(context);
        foreach (var component in middleware.Reverse())
        {
            var next = pipeline;
            pipeline = context => component.InvokeAsync(context, next);
        }

        return async context =>
        {
            using var lease = gate.TryAcquire();
            return lease is null
                ? CommandResponses.Error("PluginReloading", $"插件 {pluginId} 正在重载，请稍后再试。", context)
                : await pipeline(context);
        };
    }
}

internal sealed class LeasedCallbackHandler(
    string pluginId,
    PluginSupportedPlatforms supportedPlatforms,
    IPluginCallbackHandler inner,
    PluginInvocationGate gate,
    IReadOnlyList<IPluginCallbackMiddleware> middleware) : IPluginCallbackHandler
{
    public IReadOnlyCollection<string> ActionTypes { get; } = inner.ActionTypes
        .SelectMany(action => action.Contains(':', StringComparison.Ordinal)
            ? [action]
            : new[] { action, $"{pluginId}:{action}" })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task<CommandResponse> ExecuteAsync(
        string actionType,
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken = default)
    {
        if (!supportedPlatforms.Supports(context.Request.Platform))
        {
            return CommandResponses.Error(
                "UnsupportedPlatform",
                $"插件 {pluginId} 不支持当前平台。",
                context);
        }

        using var lease = gate.TryAcquire();
        if (lease is null)
        {
            return CommandResponses.Error("PluginReloading", $"插件 {pluginId} 正在重载，请稍后再试。", context);
        }

        var localAction = actionType.StartsWith(pluginId + ":", StringComparison.OrdinalIgnoreCase)
            ? actionType[(pluginId.Length + 1)..]
            : actionType;
        PluginCallbackHandlerDelegate pipeline = (commandContext, callbackAction, messageId, token) =>
            inner.ExecuteAsync(localAction, commandContext, callbackAction, messageId, token);
        foreach (var component in middleware.Reverse())
        {
            var next = pipeline;
            pipeline = (commandContext, callbackAction, messageId, token) =>
                component.InvokeAsync(commandContext, callbackAction, messageId, next, token);
        }

        return await pipeline(context, action, editMessageId, cancellationToken);
    }
}

internal static class PluginSupportedPlatformExtensions
{
    public static SupportedPlatforms ToCommandPlatforms(this PluginSupportedPlatforms platforms)
    {
        var result = SupportedPlatforms.None;
        if (platforms.HasFlag(PluginSupportedPlatforms.Telegram))
        {
            result |= SupportedPlatforms.Telegram;
        }

        if (platforms.HasFlag(PluginSupportedPlatforms.QQ))
        {
            result |= SupportedPlatforms.QQ;
        }

        return result;
    }

    public static bool Supports(this PluginSupportedPlatforms platforms, BotPlatform platform)
        => platform switch
        {
            BotPlatform.Telegram => platforms.HasFlag(PluginSupportedPlatforms.Telegram),
            BotPlatform.Qq => platforms.HasFlag(PluginSupportedPlatforms.QQ),
            _ => false
        };
}

internal sealed class LeasedManagedTask(
    string pluginId,
    IManagedTask inner,
    PluginInvocationGate gate) : IManagedTask
{
    public string Name => $"{pluginId}:{inner.Name}";

    public string Description => inner.Description;

    public bool Enabled
    {
        get => inner.Enabled;
        set => inner.Enabled = value;
    }

    public string Cron => inner.Cron;

    public bool IsRunning => inner.IsRunning;

    public DateTimeOffset? LastStartedAt => inner.LastStartedAt;

    public DateTimeOffset? LastCompletedAt => inner.LastCompletedAt;

    public string? LastError => inner.LastError;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var lease = gate.TryAcquire()
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is draining.");
        await inner.ExecuteAsync(cancellationToken);
    }

    public bool Cancel() => inner.Cancel();
}

internal sealed class LeasedAdminCommand(
    string pluginId,
    IAdminCommand inner,
    PluginInvocationGate gate) : IAdminCommand
{
    public AdminCommandDefinition Definition => inner.Definition;

    public async Task<AdminCommandResult> ExecuteAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        using var lease = gate.TryAcquire();
        return lease is null
            ? AdminCommandResult.Error($"Plugin '{pluginId}' is reloading.")
            : await inner.ExecuteAsync(args, cancellationToken);
    }
}

internal sealed class LeasedNotificationSource(
    string pluginId,
    IPluginNotificationSource inner,
    PluginInvocationGate gate) : IPluginNotificationSource
{
    public string Type => inner.Type;

    public string DisplayName => inner.DisplayName;

    public int Order => inner.Order;

    public NotificationCategory? Category => inner.Category;

    public UserPrivilege RequiredPrivilege => inner.RequiredPrivilege;

    public SupportedPlatforms SupportPlatforms => inner.SupportPlatforms;

    public bool Enabled => inner.Enabled;

    public async Task<bool> HasEnabledTargetsAsync(
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        using var lease = gate.TryAcquire();
        return lease is not null && await inner.HasEnabledTargetsAsync(context, cancellationToken);
    }

    public async Task<CommandResponse> BuildAccountPanelAsync(
        CommandContext context,
        string? editMessageId,
        CancellationToken cancellationToken = default)
    {
        using var lease = gate.TryAcquire();
        return lease is null
            ? CommandResponses.Error("PluginReloading", $"插件 {pluginId} 正在重载，请稍后再试。", context)
            : await inner.BuildAccountPanelAsync(context, editMessageId, cancellationToken);
    }

    public async Task<CommandResponse> ToggleAsync(
        CommandContext context,
        long accountId,
        bool toggleAll,
        string editMessageId,
        CancellationToken cancellationToken = default)
    {
        using var lease = gate.TryAcquire();
        return lease is null
            ? CommandResponses.Error("PluginReloading", $"插件 {pluginId} 正在重载，请稍后再试。", context)
            : await inner.ToggleAsync(context, accountId, toggleAll, editMessageId, cancellationToken);
    }
}
