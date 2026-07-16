using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Host.Plugins;

internal sealed class PluginBuilder(IConfiguration configuration) : IPluginBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();

    public IConfiguration Configuration { get; } = configuration;

    public ICollection<IPluginRegistration> Registrations { get; } = [];
}

internal sealed class PluginContext(
    PluginMetadata metadata,
    string pluginDirectory,
    string shadowDirectory,
    long generation,
    IConfiguration configuration,
    IPluginHostServices hostServices,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider,
    CancellationToken lifetimeToken) : IPluginContext
{
    public PluginMetadata Metadata { get; } = metadata;

    public string PluginDirectory { get; } = pluginDirectory;

    public string ShadowDirectory { get; } = shadowDirectory;

    public long Generation { get; } = generation;

    public PluginState State { get; private set; } = PluginState.Discovered;

    public IConfiguration Configuration { get; } = configuration;

    public IPluginHostServices HostServices { get; } = hostServices;

    public ILoggerFactory LoggerFactory { get; } = loggerFactory;

    public TimeProvider TimeProvider { get; } = timeProvider;

    public CancellationToken LifetimeToken { get; } = lifetimeToken;

    public void SetState(PluginState state) => State = state;
}

internal sealed class PluginInvocationGate
{
    private readonly Lock _lock = new();
    private int _active;
    private bool _draining;
    private TaskCompletionSource _drained = NewCompletionSource(completed: true);

    public IDisposable? TryAcquire()
    {
        lock (_lock)
        {
            if (_draining)
            {
                return null;
            }

            if (_active++ == 0)
            {
                _drained = NewCompletionSource(completed: false);
            }

            return new Lease(this);
        }
    }

    public Task BeginDrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_lock)
        {
            _draining = true;
            waitTask = _drained.Task;
        }

        return waitTask.WaitAsync(timeout, cancellationToken);
    }

    public void CancelDrain()
    {
        lock (_lock)
        {
            _draining = false;
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            if (--_active == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource NewCompletionSource(bool completed)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
        {
            source.SetResult();
        }

        return source;
    }

    private sealed class Lease(PluginInvocationGate owner) : IDisposable
    {
        private PluginInvocationGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

internal sealed class PluginHandle(
    PluginDescriptor descriptor,
    long generation,
    string shadowDirectory,
    PluginLoadContext loadContext,
    Assembly entryAssembly,
    BasicPlugin plugin,
    PluginContext context,
    IConfiguration configuration,
    ServiceProvider serviceProvider,
    IReadOnlyList<IPluginRegistration> registrations,
    CancellationTokenSource lifetimeCancellation)
{
    public PluginDescriptor Descriptor { get; } = descriptor;

    public long Generation { get; } = generation;

    public string ShadowDirectory { get; } = shadowDirectory;

    public PluginLoadContext LoadContext { get; } = loadContext;

    public Assembly EntryAssembly { get; } = entryAssembly;

    public BasicPlugin Plugin { get; } = plugin;

    public PluginContext Context { get; } = context;

    public IConfiguration Configuration { get; } = configuration;

    public ServiceProvider ServiceProvider { get; } = serviceProvider;

    public IReadOnlyList<IPluginRegistration> Registrations { get; } = registrations;

    public CancellationTokenSource LifetimeCancellation { get; } = lifetimeCancellation;

    public PluginInvocationGate InvocationGate { get; } = new();

    public List<IHostedService> HostedServices { get; } = [];

    public List<OhMyBot.Core.Commanding.Commands.IPlatformCommandDslProvider> CommandProviders { get; } = [];

    public List<OhMyBot.Core.Commanding.Callbacks.IPluginCallbackHandler> CallbackHandlers { get; } = [];

    public List<OhMyBot.Core.Infrastructure.ScheduledTasks.IManagedTask> ManagedTasks { get; } = [];

    public List<OhMyBot.Core.Commanding.Admin.IAdminCommand> AdminCommands { get; } = [];

    public List<OhMyBot.Core.Commanding.Notifications.IPluginNotificationSource> NotificationSources { get; } = [];

    public string? LastError { get; set; }
}
