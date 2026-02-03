using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLib.Services;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class LogMeService(ITelegramBotClient botClient, IServiceProvider provider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await botClient.GetMe(cancellationToken: cancellationToken);
        await using var scoped = provider.CreateAsyncScope();
        var userService = scoped.ServiceProvider.GetRequiredService<TelegramUserService>();
        await userService.LogUserAsync(me.Id, me.Username, me.FirstName, me.LastName, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}