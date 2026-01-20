using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Configs;

namespace OhMyTelegramBot.HostedService;

public class AutoConfigOwnerService(BotUserService botUserService, IOptionsMonitor<BotConfig> config, ILogger<AutoConfigOwnerService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = config.CurrentValue;
        if (cfg.OwnerId == 0)
            return Task.CompletedTask;

        var created = botUserService.CreateUserIfNotExists(cfg.OwnerId.ToString(), SoftwareType.Telegram, UserPrivilege.Owner);
        if (created is not null)
        {
            logger.LogInformation("Create bot owner: Id={OwnerId}", cfg.OwnerId);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}