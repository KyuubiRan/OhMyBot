using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Configs;

namespace OhMyTelegramBot.HostedServices;

public class AutoConfigOwnerService(IServiceProvider provider, IOptionsMonitor<BotConfig> config, ILogger<AutoConfigOwnerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = config.CurrentValue;
        if (cfg.OwnerId == 0)
            return;

        await using var scoped = provider.CreateAsyncScope();
        var botUserService = scoped.ServiceProvider.GetRequiredService<BotUserService>();

        var created = await botUserService.CreateUserIfNotExistsAsync(cfg.OwnerId.ToString(), SoftwareType.Telegram, UserPrivilege.Owner, cancellationToken);
        if (created is not null)
        {
            logger.LogInformation("Create bot owner: Id={OwnerId}", cfg.OwnerId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}