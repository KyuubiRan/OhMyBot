using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.Services;

namespace OhMyLib.HostedServices;

public class AutoCleanCacheFileService(ILogger<AutoCleanCacheFileService> logger, CacheFileService cacheFileService) : BackgroundService
{
    private static readonly TimeSpan ExecuteAt = new(0, 1, 0);
    private static readonly TimeSpan FileKeepDuration = TimeSpan.FromDays(3);

    private async Task Process(DirectoryInfo directoryInfo)
    {
        if (!cacheFileService.CacheDirectory.Exists)
            return;

        var files = directoryInfo.GetFiles();
        var now = DateTimeOffset.Now;
        foreach (var fileInfo in files)
        {
            try
            {
                var lastWriteTime = fileInfo.LastWriteTimeUtc;
                var age = now - lastWriteTime;
                if (age > FileKeepDuration)
                {
                    logger.LogInformation("Delete cache file: {FilePath}, last write time: {LastWriteTime:yyyy/MM/dd HH:mm:ss}, age: {Age:g}",
                                          fileInfo.FullName, lastWriteTime, age);
                    fileInfo.Delete();
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to delete cache file: {FilePath}", fileInfo.FullName);
            }
        }

        var directories = directoryInfo.GetDirectories();
        foreach (var dirInfo in directories)
        {
            await Process(dirInfo);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var today = now.Date.Add(ExecuteAt);
                var next = today > now ? today : today.AddDays(1);
                var delay = next - now;
                delay = delay.Add(TimeSpan.FromSeconds(3));

                logger.LogInformation("Next auto clean cache file execution at {ExecuteAt:yyyy/MM/dd HH:mm:ss} (in {Delay:g})", next, delay);
                await Task.Delay(delay, stoppingToken);

                await Process(cacheFileService.CacheDirectory);

                logger.LogInformation("Daily auto clean cache file task completed.");
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Daily job failed");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}