using Microsoft.Extensions.Hosting;
using OhMyLib.Services;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class LogMeService(ITelegramBotClient client, TelegramUserService userService) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await client.GetMe(cancellationToken: cancellationToken);
        await userService.LogUserAsync(me.Id, me.Username, me.FirstName, me.LastName, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}