using Microsoft.Extensions.Options;
using OhMyBot.Core.Messaging;

namespace OhMyBot.Core.Routing;

public sealed class RouteStoreHostedService(
    RouteStore routeStore,
    IRouteChangePublisher routeChangePublisher,
    IOptions<RouteOptions> options,
    ILogger<RouteStoreHostedService> logger) : BackgroundService
{
    private readonly RouteOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await routeStore.InitializeAsync(stoppingToken);

        var routeFilePath = routeStore.RouteFilePath;
        var directory = Path.GetDirectoryName(routeFilePath);
        var fileName = Path.GetFileName(routeFilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            logger.LogWarning("Route file watcher is disabled because Routes:Path is invalid: {RouteFilePath}.", routeFilePath);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        var changeSignal = new SemaphoreSlim(0);
        FileSystemEventHandler onChanged = (_, _) => changeSignal.Release();
        RenamedEventHandler onRenamed = (_, _) => changeSignal.Release();
        watcher.Changed += onChanged;
        watcher.Created += onChanged;
        watcher.Renamed += onRenamed;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await changeSignal.WaitAsync(stoppingToken);
                await Task.Delay(_options.ReloadDebounce, stoppingToken);

                while (changeSignal.CurrentCount > 0)
                {
                    await changeSignal.WaitAsync(stoppingToken);
                }

                if (await routeStore.ReloadAsync(writeMergedFile: true, stoppingToken))
                {
                    await routeChangePublisher.PublishRoutesChangedAsync(routeStore.Version, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            watcher.Changed -= onChanged;
            watcher.Created -= onChanged;
            watcher.Renamed -= onRenamed;
        }
    }
}
