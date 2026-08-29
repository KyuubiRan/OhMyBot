using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Infrastructure.Plugins;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Plugin.Abstractions;
using OhMyBot.Plugin.Commanding;

namespace OhMyBot.Core.Host.Plugins;

internal sealed class PluginManager : IPluginManager, IHostedService
{
    private const string CoreApiVersion = "1.0.0";
    private readonly IServiceProvider _rootServices;
    private readonly PlatformCommandDslRegistry _commandRegistry;
    private readonly PluginCallbackRegistry _callbackRegistry;
    private readonly ManagedTaskRegistry _taskRegistry;
    private readonly PluginAdminCommandRegistry _adminCommandRegistry;
    private readonly PluginNotificationSourceRegistry _notificationSourceRegistry;
    private readonly RouteStore _routeStore;
    private readonly IRouteChangePublisher _routeChangePublisher;
    private readonly IPluginRuntimeStateStore _stateStore;
    private readonly ILogger<PluginManager> _logger;
    private readonly PluginRuntimeOptions _options;
    private readonly HashSet<string> _disabledPluginIds;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly Lock _snapshotLock = new();
    private readonly Dictionary<string, PluginHandle> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginHandle> _staging = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginRuntimeInfo> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly PluginHostServices _hostServices;
    private long _generation;

    public PluginManager(
        IServiceProvider rootServices,
        PlatformCommandDslRegistry commandRegistry,
        PluginCallbackRegistry callbackRegistry,
        ManagedTaskRegistry taskRegistry,
        PluginAdminCommandRegistry adminCommandRegistry,
        PluginNotificationSourceRegistry notificationSourceRegistry,
        RouteStore routeStore,
        IRouteChangePublisher routeChangePublisher,
        IOptions<PluginRuntimeOptions> options,
        IPluginRuntimeStateStore stateStore,
        ILogger<PluginManager> logger)
    {
        _rootServices = rootServices;
        _commandRegistry = commandRegistry;
        _callbackRegistry = callbackRegistry;
        _taskRegistry = taskRegistry;
        _adminCommandRegistry = adminCommandRegistry;
        _notificationSourceRegistry = notificationSourceRegistry;
        _routeStore = routeStore;
        _routeChangePublisher = routeChangePublisher;
        _options = options.Value;
        _stateStore = stateStore;
        _disabledPluginIds = (stateStore.LoadDisabledPluginIds() ?? _options.DisabledPluginIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _hostServices = new PluginHostServices(rootServices, FindAvailablePlugin);
    }

    public IReadOnlyList<PluginRuntimeInfo> GetPlugins()
    {
        lock (_snapshotLock)
        {
            return _status.Values
                .OrderByDescending(plugin => plugin.LoadPriority)
                .ThenBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReloadAsync("all", cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Initial plugin discovery failed. Core will continue without runtime plugins.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var handles = GetActiveHandlesInLoadOrder().Reverse().ToArray();
            foreach (var handle in handles)
            {
                handle.Context.SetState(PluginState.Draining);
                try
                {
                    await handle.InvocationGate.BeginDrainAsync(_options.DrainTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timed out draining plugin {PluginId} during shutdown.", handle.Descriptor.Metadata.Id);
                }
            }

            foreach (var handle in handles)
            {
                UnregisterComponents(handle);
                await StopHandleAsync(handle, cancellationToken);
                await DisposeHandleAsync(handle);
            }

            lock (_snapshotLock)
            {
                _active.Clear();
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<PluginReloadResult> ReloadAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await ReloadCoreAsync(pluginId, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<PluginActivationResult> DisableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await DisableCoreAsync(pluginId.Trim(), cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<PluginActivationResult> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await EnableCoreAsync(pluginId.Trim(), cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<PluginActivationResult> DisableCoreAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(pluginId, "all", StringComparison.OrdinalIgnoreCase))
        {
            return new PluginActivationResult(false, "plugin disable 需要指定单个插件 id。");
        }

        PluginDiscoveryResult discovery;
        try
        {
            discovery = PluginDiscovery.DiscoverDetailed(ResolveRuntimePath(_options.PluginPath));
        }
        catch (Exception exception)
        {
            return new PluginActivationResult(false, $"插件发现失败：{exception.GetBaseException().Message}");
        }

        var descriptor = discovery.Descriptors.FirstOrDefault(item =>
            string.Equals(item.Metadata.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return new PluginActivationResult(false, $"未找到插件：{pluginId}");
        }

        if (_disabledPluginIds.Contains(pluginId))
        {
            UpdateDisabledStatuses([descriptor]);
            try
            {
                await PersistDisabledPluginIdsAsync(cancellationToken);
                return new PluginActivationResult(true, $"插件 {descriptor.Metadata.Id} 已处于禁用状态。");
            }
            catch (Exception exception)
            {
                return new PluginActivationResult(
                    false,
                    $"插件 {descriptor.Metadata.Id} 已禁用，但持久化状态失败：{exception.GetBaseException().Message}");
            }
        }

        var activeDescriptors = GetActiveHandlesInLoadOrder()
            .Select(handle => handle.Descriptor)
            .ToArray();
        var activeDependents = BuildReloadClosure(descriptor.Metadata.Id, activeDescriptors)
            .Where(id => !string.Equals(id, descriptor.Metadata.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activeDependents.Length > 0)
        {
            return new PluginActivationResult(
                false,
                $"无法禁用插件 {descriptor.Metadata.Id}；以下活动插件依赖它：{string.Join(", ", activeDependents)}。");
        }

        _disabledPluginIds.Add(descriptor.Metadata.Id);
        try
        {
            var active = FindActivePlugin(descriptor.Metadata.Id);
            if (active is not null)
            {
                await UnloadActiveHandleAsync(active, cancellationToken);
            }

            UpdateDisabledStatuses([descriptor]);
            await PersistDisabledPluginIdsAsync(cancellationToken);
            return new PluginActivationResult(true, $"已禁用插件 {descriptor.Metadata.Id}。");
        }
        catch (Exception exception)
        {
            _disabledPluginIds.Remove(descriptor.Metadata.Id);
            var rollback = FindActivePlugin(descriptor.Metadata.Id) is null
                ? await ReloadCoreAsync(descriptor.Metadata.Id, cancellationToken)
                : null;
            var rollbackMessage = rollback is null || rollback.Success
                ? string.Empty
                : $" 回滚加载失败：{rollback.Message}";
            return new PluginActivationResult(
                false,
                $"禁用插件 {descriptor.Metadata.Id} 失败：{exception.GetBaseException().Message}.{rollbackMessage}");
        }
    }

    private async Task<PluginActivationResult> EnableCoreAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(pluginId, "all", StringComparison.OrdinalIgnoreCase))
        {
            return new PluginActivationResult(false, "plugin enable 需要指定单个插件 id。");
        }

        var wasDisabled = _disabledPluginIds.Remove(pluginId);
        var reload = await ReloadCoreAsync(pluginId, cancellationToken);
        if (!reload.Success)
        {
            if (wasDisabled)
            {
                _disabledPluginIds.Add(pluginId);
                MarkDiscoveredPluginDisabled(pluginId);
            }

            return new PluginActivationResult(false, $"启用插件 {pluginId} 失败：{reload.Message}");
        }

        if (!wasDisabled)
        {
            return new PluginActivationResult(true, $"插件 {pluginId} 已处于启用状态并完成重载。");
        }

        try
        {
            await PersistDisabledPluginIdsAsync(cancellationToken);
            return new PluginActivationResult(true, $"已启用插件 {pluginId}。");
        }
        catch (Exception exception)
        {
            _disabledPluginIds.Add(pluginId);
            var active = FindActivePlugin(pluginId);
            if (active is not null)
            {
                try
                {
                    await UnloadActiveHandleAsync(active, cancellationToken);
                }
                catch (Exception rollbackException)
                {
                    return new PluginActivationResult(
                        false,
                        $"启用状态持久化失败：{exception.GetBaseException().Message}；回滚卸载也失败：{rollbackException.GetBaseException().Message}");
                }
            }

            MarkDiscoveredPluginDisabled(pluginId);
            return new PluginActivationResult(
                false,
                $"启用插件 {pluginId} 后无法持久化状态，已回滚：{exception.GetBaseException().Message}");
        }
    }

    private Task PersistDisabledPluginIdsAsync(CancellationToken cancellationToken)
    {
        return _stateStore.SaveDisabledPluginIdsAsync(
            _disabledPluginIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            cancellationToken);
    }

    private void MarkDiscoveredPluginDisabled(string pluginId)
    {
        var discovery = PluginDiscovery.DiscoverDetailed(ResolveRuntimePath(_options.PluginPath));
        var descriptor = discovery.Descriptors.FirstOrDefault(item =>
            string.Equals(item.Metadata.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is not null)
        {
            UpdateDisabledStatuses([descriptor]);
        }
    }

    private async Task<PluginReloadResult> ReloadCoreAsync(string pluginId, CancellationToken cancellationToken)
    {
        PluginDiscoveryResult discovery;
        try
        {
            discovery = PluginDiscovery.DiscoverDetailed(ResolveRuntimePath(_options.PluginPath));
        }
        catch (Exception exception)
        {
            return new PluginReloadResult(false, $"插件发现失败：{exception.GetBaseException().Message}", []);
        }

        var disabledDescriptors = discovery.Descriptors
            .Where(descriptor => _disabledPluginIds.Contains(descriptor.Metadata.Id))
            .ToArray();
        var descriptors = discovery.Descriptors
            .Where(descriptor => !_disabledPluginIds.Contains(descriptor.Metadata.Id))
            .ToArray();
        var validation = ValidateAndSortFailSoft(descriptors);
        var loadOrder = validation.LoadOrder;

        PruneInactiveStatuses(discovery.Descriptors, discovery.Failures);
        UpdateDiscoveredStatuses(descriptors, null);
        UpdateDisabledStatuses(disabledDescriptors);
        UpdateDiscoveryFailures(discovery.Failures);
        UpdateValidationFailures(validation.Failures);
        var reloadAll = string.Equals(pluginId, "all", StringComparison.OrdinalIgnoreCase);
        if (!reloadAll && _disabledPluginIds.Contains(pluginId))
        {
            return new PluginReloadResult(
                false,
                $"插件 {pluginId} 已通过 PluginRuntime:DisabledPluginIds 禁用。",
                []);
        }

        var descriptorMap = loadOrder.ToDictionary(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        if (!reloadAll && !descriptorMap.ContainsKey(pluginId))
        {
            var validationError = validation.Failures
                .FirstOrDefault(item => string.Equals(
                    item.Descriptor.Metadata.Id,
                    pluginId,
                    StringComparison.OrdinalIgnoreCase));
            if (validationError is not null)
            {
                return new PluginReloadResult(
                    false,
                    $"插件 {pluginId} 校验失败：{validationError.Error}",
                    []);
            }

            return new PluginReloadResult(false, $"未找到插件：{pluginId}", []);
        }

        var closure = reloadAll
            ? loadOrder.Select(item => item.Metadata.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : BuildReloadClosure(pluginId, loadOrder);
        var candidates = loadOrder.Where(item => closure.Contains(item.Metadata.Id)).ToArray();
        var oldHandles = GetActiveHandlesInLoadOrder().Where(handle => closure.Contains(handle.Descriptor.Metadata.Id)).ToArray();

        var staged = new List<PluginHandle>();
        try
        {
            foreach (var descriptor in candidates)
            {
                var missingActiveDependency = descriptor.Metadata.Dependencies
                    .Where(dependency => dependency.Required && !closure.Contains(dependency.PluginId))
                    .FirstOrDefault(dependency => FindAvailablePlugin(dependency.PluginId) is null);
                if (missingActiveDependency is not null)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{descriptor.Metadata.Id}' requires inactive plugin '{missingActiveDependency.PluginId}'.");
                }

                var handle = await CreateHandleAsync(descriptor, cancellationToken);
                staged.Add(handle);
                lock (_snapshotLock)
                {
                    _staging[descriptor.Metadata.Id] = handle;
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var handle in staged.AsEnumerable().Reverse())
            {
                await DisposeHandleAsync(handle);
            }

            lock (_snapshotLock)
            {
                _staging.Clear();
            }

            SetFaulted(candidates, exception.GetBaseException().Message);
            return new PluginReloadResult(false, $"插件候选版本初始化失败：{exception.GetBaseException().Message}", []);
        }

        try
        {
            foreach (var handle in oldHandles)
            {
                handle.Context.SetState(PluginState.Draining);
            }

            await Task.WhenAll(oldHandles.Select(handle =>
                handle.InvocationGate.BeginDrainAsync(_options.DrainTimeout, cancellationToken)));

            foreach (var handle in oldHandles.Reverse())
            {
                UnregisterComponents(handle);
            }

            if (oldHandles.Length > 0)
            {
                await RebuildRoutesAsync(cancellationToken);
            }

            foreach (var handle in oldHandles.Reverse())
            {
                await StopHandleAsync(handle, cancellationToken);
            }

            var started = new List<PluginHandle>();
            try
            {
                foreach (var handle in staged)
                {
                    await StartHandleAsync(handle, cancellationToken);
                    RegisterComponents(handle);
                    started.Add(handle);
                }

                await RebuildRoutesAsync(cancellationToken);
            }
            catch
            {
                foreach (var handle in started.AsEnumerable().Reverse())
                {
                    UnregisterComponents(handle);
                    await StopHandleAsync(handle, cancellationToken);
                }

                foreach (var handle in oldHandles)
                {
                    handle.InvocationGate.CancelDrain();
                    await StartHandleAsync(handle, cancellationToken);
                    RegisterComponents(handle);
                }

                await RebuildRoutesAsync(cancellationToken);
                throw;
            }

            lock (_snapshotLock)
            {
                foreach (var handle in oldHandles)
                {
                    _active.Remove(handle.Descriptor.Metadata.Id);
                }

                foreach (var handle in staged)
                {
                    _active[handle.Descriptor.Metadata.Id] = handle;
                    _staging.Remove(handle.Descriptor.Metadata.Id);
                    UpdateStatus(handle, null);
                }
            }

            foreach (var handle in oldHandles.Reverse())
            {
                await DisposeHandleAsync(handle);
            }

            var skippedCount = discovery.Failures.Count + validation.Failures.Count;
            var message = staged.Count == 0
                ? "没有需要重载的插件。"
                : $"已重载 {staged.Count} 个插件。";
            if (skippedCount > 0)
            {
                message += $" 已跳过 {skippedCount} 个发现或校验失败的插件。";
            }

            return new PluginReloadResult(
                true,
                message,
                staged.Select(handle => handle.Descriptor.Metadata.Id).ToArray());
        }
        catch (Exception exception)
        {
            foreach (var handle in staged.AsEnumerable().Reverse())
            {
                await DisposeHandleAsync(handle);
            }

            lock (_snapshotLock)
            {
                _staging.Clear();
            }

            SetFaulted(candidates, exception.GetBaseException().Message);
            return new PluginReloadResult(false, $"插件重载失败：{exception.GetBaseException().Message}", []);
        }
    }

    private async Task<PluginHandle> CreateHandleAsync(
        PluginDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _generation);
        var shadowDirectory = CreateShadowCopy(descriptor, generation);
        var shadowEntryPath = Path.Combine(shadowDirectory, "Plugin.dll");
        var lifetimeCancellation = new CancellationTokenSource();
        var loadContext = new PluginLoadContext(
            shadowEntryPath,
            assemblyName => ResolveDependencyAssembly(descriptor, assemblyName));
        var entryAssembly = loadContext.LoadFromAssemblyPath(shadowEntryPath);
        var entryType = entryAssembly.GetType(descriptor.EntryTypeName, throwOnError: true)!;
        if (!typeof(BasicPlugin).IsAssignableFrom(entryType))
        {
            throw new InvalidOperationException($"Plugin entry type '{descriptor.EntryTypeName}' has an incompatible BasicPlugin identity.");
        }

        var plugin = (BasicPlugin?)Activator.CreateInstance(entryType)
            ?? throw new InvalidOperationException($"Unable to create plugin '{descriptor.Metadata.Id}'.");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(descriptor.SourceDirectory)
            .AddJsonFile("pluginsettings.json", optional: true, reloadOnChange: false)
            .Build();
        var context = new PluginContext(
            descriptor.Metadata,
            descriptor.SourceDirectory,
            shadowDirectory,
            generation,
            configuration,
            _hostServices,
            _rootServices.GetRequiredService<ILoggerFactory>(),
            _rootServices.GetRequiredService<TimeProvider>(),
            lifetimeCancellation.Token);
        context.SetState(PluginState.Validating);

        var builder = new PluginBuilder(configuration);
        builder.Services.TryAddSingleton<IConfiguration>(configuration);
        builder.Services.TryAddSingleton<IPluginContext>(context);
        builder.Services.TryAddSingleton<IPluginHostServices>(_hostServices);
        builder.Services.TryAddSingleton(_rootServices.GetRequiredService<ILoggerFactory>());
        builder.Services.TryAddSingleton(_rootServices.GetRequiredService<TimeProvider>());
        plugin.Configure(builder);
        context.SetState(PluginState.Configured);

        foreach (var export in builder.Registrations.OfType<PluginExportRegistration>())
        {
            if (export.Lifetime != ServiceLifetime.Singleton)
            {
                throw new InvalidOperationException(
                    $"Export '{export.ContractType.FullName}' in plugin '{descriptor.Metadata.Id}' must be singleton.");
            }
        }

        var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var handle = new PluginHandle(
            descriptor,
            generation,
            shadowDirectory,
            loadContext,
            entryAssembly,
            plugin,
            context,
            configuration,
            provider,
            builder.Registrations.ToArray(),
            lifetimeCancellation);
        PopulateComponents(handle);

        await plugin.InitializeAsync(context, cancellationToken);
        foreach (var migration in provider.GetServices<IPluginDatabaseMigration>())
        {
            await migration.MigrateAsync(cancellationToken);
        }

        context.SetState(PluginState.Initialized);
        UpdateStatus(handle, null);
        return handle;
    }

    private void PopulateComponents(PluginHandle handle)
    {
        var commands = new List<IPlatformCommandDslProvider>();
        var callbacks = new List<IPluginCallbackHandler>();
        var commandMiddleware = new List<IPluginCommandMiddleware>();
        var callbackMiddleware = new List<IPluginCallbackMiddleware>();
        foreach (var registration in handle.Registrations.OfType<IPluginComponentRegistration>())
        {
            var component = handle.ServiceProvider.GetRequiredService(registration.ImplementationType);
            switch (registration.Kind)
            {
                case "PlatformCommand":
                    commands.Add((IPlatformCommandDslProvider)component);
                    break;
                case "CallbackHandler":
                    callbacks.Add((IPluginCallbackHandler)component);
                    break;
                case "ManagedTask":
                    handle.ManagedTasks.Add(new LeasedManagedTask(
                        handle.Descriptor.Metadata.Id,
                        (IManagedTask)component,
                        handle.InvocationGate));
                    break;
                case "HostedService":
                    handle.HostedServices.Add((IHostedService)component);
                    break;
                case "AdminCommand":
                    handle.AdminCommands.Add(new LeasedAdminCommand(
                        handle.Descriptor.Metadata.Id,
                        (IAdminCommand)component,
                        handle.InvocationGate));
                    break;
                case "CommandMiddleware":
                    commandMiddleware.Add((IPluginCommandMiddleware)component);
                    break;
                case "CallbackMiddleware":
                    callbackMiddleware.Add((IPluginCallbackMiddleware)component);
                    break;
                case "NotificationSource":
                    handle.NotificationSources.Add(new LeasedNotificationSource(
                        handle.Descriptor.Metadata.Id,
                        (IPluginNotificationSource)component,
                        handle.InvocationGate));
                    break;
            }
        }

        handle.CommandProviders.AddRange(commands.Select(command => new LeasedCommandProvider(
            handle.Descriptor.Metadata.Id,
            handle.Descriptor.Metadata.SupportedPlatforms,
            command,
            handle.InvocationGate,
            commandMiddleware)));
        handle.CallbackHandlers.AddRange(callbacks.Select(callback => new LeasedCallbackHandler(
            handle.Descriptor.Metadata.Id,
            handle.Descriptor.Metadata.SupportedPlatforms,
            callback,
            handle.InvocationGate,
            callbackMiddleware)));
    }

    private async Task StartHandleAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        handle.Context.SetState(PluginState.Starting);
        await handle.Plugin.StartAsync(handle.Context, cancellationToken);
        foreach (var hostedService in handle.HostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        handle.Context.SetState(PluginState.Active);
        handle.InvocationGate.CancelDrain();
        UpdateStatus(handle, null);
    }

    private async Task StopHandleAsync(PluginHandle handle, CancellationToken cancellationToken)
    {
        handle.Context.SetState(PluginState.Stopping);
        foreach (var hostedService in handle.HostedServices.AsEnumerable().Reverse())
        {
            await hostedService.StopAsync(cancellationToken);
        }

        await handle.Plugin.StopAsync(handle.Context, cancellationToken);
        UpdateStatus(handle, null);
    }

    private async Task UnloadActiveHandleAsync(
        PluginHandle handle,
        CancellationToken cancellationToken)
    {
        handle.Context.SetState(PluginState.Draining);
        UpdateStatus(handle, null);
        try
        {
            await handle.InvocationGate.BeginDrainAsync(_options.DrainTimeout, cancellationToken);
        }
        catch
        {
            handle.InvocationGate.CancelDrain();
            handle.Context.SetState(PluginState.Active);
            UpdateStatus(handle, null);
            throw;
        }

        UnregisterComponents(handle);
        try
        {
            await RebuildRoutesAsync(cancellationToken);
        }
        catch
        {
            RegisterComponents(handle);
            handle.InvocationGate.CancelDrain();
            handle.Context.SetState(PluginState.Active);
            UpdateStatus(handle, null);
            throw;
        }

        try
        {
            await StopHandleAsync(handle, cancellationToken);
        }
        catch
        {
            RegisterComponents(handle);
            handle.InvocationGate.CancelDrain();
            handle.Context.SetState(PluginState.Active);
            UpdateStatus(handle, null);
            await RebuildRoutesAsync(cancellationToken);
            throw;
        }

        lock (_snapshotLock)
        {
            _active.Remove(handle.Descriptor.Metadata.Id);
        }

        await DisposeHandleAsync(handle);
    }

    private async ValueTask DisposeHandleAsync(PluginHandle handle)
    {
        handle.LifetimeCancellation.Cancel();
        await handle.Plugin.DisposeAsync();
        await handle.ServiceProvider.DisposeAsync();
        if (handle.Configuration is IDisposable disposableConfiguration)
        {
            disposableConfiguration.Dispose();
        }

        handle.LifetimeCancellation.Dispose();
        handle.Context.SetState(PluginState.Disposed);
        var weakReference = new WeakReference(handle.LoadContext, trackResurrection: false);
        handle.LoadContext.Unload();
        handle.Context.SetState(PluginState.Unloaded);
        for (var attempt = 0; attempt < 2 && weakReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (weakReference.IsAlive)
        {
            _logger.LogWarning("Plugin load context for {PluginId} generation {Generation} is still alive.",
                handle.Descriptor.Metadata.Id,
                handle.Generation);
        }

        TryDeleteShadowDirectory(handle.ShadowDirectory);
    }

    private void RegisterComponents(PluginHandle handle)
    {
        var id = handle.Descriptor.Metadata.Id;
        _commandRegistry.RegisterPlugin(id, handle.CommandProviders);
        try
        {
            _callbackRegistry.RegisterPlugin(id, handle.CallbackHandlers);
            try
            {
                _taskRegistry.RegisterPlugin(id, handle.ManagedTasks);
                try
                {
                    _adminCommandRegistry.RegisterPlugin(id, handle.AdminCommands);
                    try
                    {
                        _notificationSourceRegistry.RegisterPlugin(id, handle.NotificationSources);
                    }
                    catch
                    {
                        _adminCommandRegistry.UnregisterPlugin(id);
                        throw;
                    }
                }
                catch
                {
                    _taskRegistry.UnregisterPlugin(id);
                    throw;
                }
            }
            catch
            {
                _callbackRegistry.UnregisterPlugin(id);
                throw;
            }
        }
        catch
        {
            _commandRegistry.UnregisterPlugin(id);
            throw;
        }
    }

    private void UnregisterComponents(PluginHandle handle)
    {
        var id = handle.Descriptor.Metadata.Id;
        _notificationSourceRegistry.UnregisterPlugin(id);
        _adminCommandRegistry.UnregisterPlugin(id);
        _taskRegistry.UnregisterPlugin(id);
        _callbackRegistry.UnregisterPlugin(id);
        _commandRegistry.UnregisterPlugin(id);
    }

    private async Task RebuildRoutesAsync(CancellationToken cancellationToken)
    {
        if (!await _routeStore.ReloadAsync(writeMergedFile: true, cancellationToken))
        {
            throw new InvalidOperationException("Failed to rebuild command routes after plugin change.");
        }

        await _routeChangePublisher.PublishRoutesChangedAsync(_routeStore.Version, cancellationToken);
    }

    private PluginHandle? FindAvailablePlugin(string pluginId)
    {
        lock (_snapshotLock)
        {
            return _staging.GetValueOrDefault(pluginId) ?? _active.GetValueOrDefault(pluginId);
        }
    }

    private PluginHandle? FindActivePlugin(string pluginId)
    {
        lock (_snapshotLock)
        {
            return _active.GetValueOrDefault(pluginId);
        }
    }

    private Assembly? ResolveDependencyAssembly(
        PluginDescriptor requester,
        AssemblyName assemblyName)
    {
        lock (_snapshotLock)
        {
            foreach (var dependency in requester.Metadata.Dependencies)
            {
                var handle = _staging.GetValueOrDefault(dependency.PluginId)
                             ?? _active.GetValueOrDefault(dependency.PluginId);
                if (handle is null)
                {
                    continue;
                }

                if (!VersionRange.Parse(dependency.VersionRange)
                        .Satisfies(NuGetVersion.Parse(handle.Descriptor.Metadata.Version)))
                {
                    continue;
                }

                var loaded = handle.LoadContext.Assemblies.FirstOrDefault(assembly =>
                    AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
                if (loaded is not null)
                {
                    return loaded;
                }

                var localPath = Path.Combine(handle.ShadowDirectory, assemblyName.Name + ".dll");
                if (!File.Exists(localPath)
                    || !AssemblyName.ReferenceMatchesDefinition(AssemblyName.GetAssemblyName(localPath), assemblyName))
                {
                    continue;
                }

                return handle.LoadContext.LoadFromAssemblyPath(localPath);
            }

            return null;
        }
    }

    private IReadOnlyList<PluginHandle> GetActiveHandlesInLoadOrder()
    {
        lock (_snapshotLock)
        {
            if (_active.Count == 0)
            {
                return [];
            }

            return ValidateAndSort(_active.Values.Select(handle => handle.Descriptor).ToArray())
                .Select(descriptor => _active[descriptor.Metadata.Id])
                .ToArray();
        }
    }

    internal static IReadOnlyList<PluginDescriptor> ValidateAndSort(IReadOnlyList<PluginDescriptor> descriptors)
    {
        var result = ValidateAndSortFailSoft(descriptors);
        if (result.Failures.Count > 0)
        {
            throw new InvalidOperationException(result.Failures[0].Error);
        }

        return result.LoadOrder;
    }

    internal static PluginValidationResult ValidateAndSortFailSoft(IReadOnlyList<PluginDescriptor> descriptors)
    {
        var failures = new Dictionary<PluginDescriptor, string>();
        foreach (var group in descriptors
                     .GroupBy(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var descriptor in group)
            {
                failures[descriptor] = $"Duplicate plugin id '{group.Key}'.";
            }
        }

        foreach (var group in descriptors
                     .GroupBy(item => item.AssemblyName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var descriptor in group)
            {
                failures[descriptor] = $"Duplicate plugin AssemblyName '{group.Key}'.";
            }
        }

        var coreVersion = NuGetVersion.Parse(CoreApiVersion);
        foreach (var descriptor in descriptors)
        {
            if (failures.ContainsKey(descriptor))
            {
                continue;
            }

            if (descriptor.Metadata.CoreApi != "*"
                && !VersionRange.Parse(descriptor.Metadata.CoreApi).Satisfies(coreVersion))
            {
                failures[descriptor] =
                    $"Plugin '{descriptor.Metadata.Id}' does not support Core API {CoreApiVersion}.";
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            var availableById = descriptors
                .Where(descriptor => !failures.ContainsKey(descriptor))
                .ToDictionary(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in descriptors.Where(item => !failures.ContainsKey(item)))
            {
                foreach (var dependency in descriptor.Metadata.Dependencies.Where(item => item.Required))
                {
                    if (!availableById.TryGetValue(dependency.PluginId, out var target))
                    {
                        failures[descriptor] =
                            $"Plugin '{descriptor.Metadata.Id}' requires missing or invalid plugin '{dependency.PluginId}'.";
                        changed = true;
                        break;
                    }

                    if (!VersionRange.Parse(dependency.VersionRange)
                            .Satisfies(NuGetVersion.Parse(target.Metadata.Version)))
                    {
                        failures[descriptor] =
                            $"Plugin '{descriptor.Metadata.Id}' requires '{dependency.PluginId}' {dependency.VersionRange}, found {target.Metadata.Version}.";
                        changed = true;
                        break;
                    }
                }
            }
        }

        var valid = descriptors.Where(descriptor => !failures.ContainsKey(descriptor)).ToArray();
        var map = valid.ToDictionary(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var indegree = valid.ToDictionary(item => item.Metadata.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var dependents = valid.ToDictionary(
            item => item.Metadata.Id,
            _ => new List<PluginDescriptor>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in valid)
        {
            foreach (var dependency in descriptor.Metadata.Dependencies)
            {
                if (!map.TryGetValue(dependency.PluginId, out var target)
                    || !VersionRange.Parse(dependency.VersionRange)
                        .Satisfies(NuGetVersion.Parse(target.Metadata.Version)))
                {
                    continue;
                }

                indegree[descriptor.Metadata.Id]++;
                dependents[dependency.PluginId].Add(descriptor);
            }
        }

        var ready = valid.Where(item => indegree[item.Metadata.Id] == 0).ToList();
        var ordered = new List<PluginDescriptor>(valid.Length);
        while (ready.Count > 0)
        {
            var next = ready
                .OrderByDescending(item => item.Metadata.LoadPriority)
                .ThenBy(item => item.Metadata.Id, StringComparer.OrdinalIgnoreCase)
                .First();
            ready.Remove(next);
            ordered.Add(next);
            foreach (var dependent in dependents[next.Metadata.Id])
            {
                if (--indegree[dependent.Metadata.Id] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (ordered.Count != valid.Length)
        {
            var blocked = indegree.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var message = $"Plugin dependency cycle detected or blocked by cycle: {string.Join(", ", blocked)}.";
            foreach (var descriptor in valid.Where(item => blocked.Contains(item.Metadata.Id)))
            {
                failures[descriptor] = message;
            }

            ordered.RemoveAll(descriptor => failures.ContainsKey(descriptor));
        }

        return new PluginValidationResult(
            ordered,
            failures.Select(pair => new PluginValidationFailure(pair.Key, pair.Value)).ToArray());
    }

    private static HashSet<string> BuildReloadClosure(string pluginId, IReadOnlyList<PluginDescriptor> descriptors)
    {
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var descriptor in descriptors)
            {
                if (!closure.Contains(descriptor.Metadata.Id)
                    && descriptor.Metadata.Dependencies.Any(dependency => closure.Contains(dependency.PluginId)))
                {
                    closure.Add(descriptor.Metadata.Id);
                    changed = true;
                }
            }
        }

        return closure;
    }

    private string CreateShadowCopy(PluginDescriptor descriptor, long generation)
    {
        var shadowDirectory = Path.Combine(
            ResolveRuntimePath(_options.ShadowPath),
            descriptor.Metadata.Id,
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(shadowDirectory))
        {
            Directory.Delete(shadowDirectory, recursive: true);
        }

        Directory.CreateDirectory(shadowDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(descriptor.SourceDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (string.Equals(fileName, "pluginsettings.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "pluginsettings.template.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(descriptor.SourceDirectory, sourcePath);
            var destinationPath = Path.Combine(shadowDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return shadowDirectory;
    }

    private void UpdateDiscoveredStatuses(IReadOnlyList<PluginDescriptor> descriptors, string? error)
    {
        lock (_snapshotLock)
        {
            foreach (var descriptor in descriptors)
            {
                if (_active.TryGetValue(descriptor.Metadata.Id, out var active))
                {
                    UpdateStatus(active, active.LastError);
                    continue;
                }

                _status[descriptor.Metadata.Id] = new PluginRuntimeInfo(
                    descriptor.Metadata.Id,
                    descriptor.Metadata.Name,
                    descriptor.Metadata.Version,
                    descriptor.Metadata.LoadPriority,
                    descriptor.Metadata.SupportedPlatforms,
                    error is null ? PluginState.Discovered : PluginState.Faulted,
                    0,
                    descriptor.Metadata.Dependencies,
                    error);
            }
        }
    }

    private void UpdateDisabledStatuses(IEnumerable<PluginDescriptor> descriptors)
    {
        lock (_snapshotLock)
        {
            foreach (var descriptor in descriptors)
            {
                _status[descriptor.Metadata.Id] = new PluginRuntimeInfo(
                    descriptor.Metadata.Id,
                    descriptor.Metadata.Name,
                    descriptor.Metadata.Version,
                    descriptor.Metadata.LoadPriority,
                    descriptor.Metadata.SupportedPlatforms,
                    PluginState.Disabled,
                    0,
                    descriptor.Metadata.Dependencies,
                    null);
            }
        }
    }

    private void PruneInactiveStatuses(
        IEnumerable<PluginDescriptor> descriptors,
        IEnumerable<PluginDiscoveryFailure> discoveryFailures)
    {
        var discoveredIds = descriptors
            .Select(descriptor => descriptor.Metadata.Id)
            .Concat(discoveryFailures.Select(failure => "invalid:" + Path.GetFileName(failure.SourceDirectory)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_snapshotLock)
        {
            foreach (var staleId in _status.Keys
                         .Where(id => !_active.ContainsKey(id) && !discoveredIds.Contains(id))
                         .ToArray())
            {
                _status.Remove(staleId);
            }
        }
    }

    private void UpdateDiscoveryFailures(IEnumerable<PluginDiscoveryFailure> failures)
    {
        lock (_snapshotLock)
        {
            foreach (var failure in failures)
            {
                var directoryName = Path.GetFileName(failure.SourceDirectory);
                var id = "invalid:" + directoryName;
                _status[id] = new PluginRuntimeInfo(
                    id,
                    directoryName,
                    "0.0.0",
                    0,
                    PluginSupportedPlatforms.None,
                    PluginState.Faulted,
                    0,
                    [],
                    failure.Error);
            }
        }
    }

    private void UpdateValidationFailures(IEnumerable<PluginValidationFailure> failures)
    {
        lock (_snapshotLock)
        {
            foreach (var failure in failures)
            {
                if (_active.TryGetValue(failure.Descriptor.Metadata.Id, out var active))
                {
                    active.LastError = failure.Error;
                    UpdateStatus(active, failure.Error);
                    continue;
                }

                _status[failure.Descriptor.Metadata.Id] = new PluginRuntimeInfo(
                    failure.Descriptor.Metadata.Id,
                    failure.Descriptor.Metadata.Name,
                    failure.Descriptor.Metadata.Version,
                    failure.Descriptor.Metadata.LoadPriority,
                    failure.Descriptor.Metadata.SupportedPlatforms,
                    PluginState.Faulted,
                    0,
                    failure.Descriptor.Metadata.Dependencies,
                    failure.Error);
            }
        }
    }

    private void SetFaulted(IEnumerable<PluginDescriptor> descriptors, string error)
    {
        lock (_snapshotLock)
        {
            foreach (var descriptor in descriptors)
            {
                _status[descriptor.Metadata.Id] = new PluginRuntimeInfo(
                    descriptor.Metadata.Id,
                    descriptor.Metadata.Name,
                    descriptor.Metadata.Version,
                    descriptor.Metadata.LoadPriority,
                    descriptor.Metadata.SupportedPlatforms,
                    PluginState.Faulted,
                    0,
                    descriptor.Metadata.Dependencies,
                    error);
            }
        }
    }

    private void UpdateStatus(PluginHandle handle, string? error)
    {
        lock (_snapshotLock)
        {
            _status[handle.Descriptor.Metadata.Id] = new PluginRuntimeInfo(
                handle.Descriptor.Metadata.Id,
                handle.Descriptor.Metadata.Name,
                handle.Descriptor.Metadata.Version,
                handle.Descriptor.Metadata.LoadPriority,
                handle.Descriptor.Metadata.SupportedPlatforms,
                handle.Context.State,
                handle.Generation,
                handle.Descriptor.Metadata.Dependencies,
                error);
        }
    }

    private void TryDeleteShadowDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to delete plugin shadow directory {ShadowDirectory}.", directory);
        }
    }

    private static string ResolveRuntimePath(string path)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, AppContext.BaseDirectory);
}
