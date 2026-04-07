using System.Text;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyLib.Services.Kuro;

namespace OhMyLib.HostedServices;

public abstract class KuroAutoSignService(ILogger logger, IServiceScopeFactory serviceFactory) : BackgroundService
{
    private static readonly TimeSpan ExecuteAt = new(0, 10, 0);

    protected abstract SoftwareType Software { get; }

    protected abstract Task SendMessage(string chatId, string message, CancellationToken cancellationToken);

    private async Task ProcessSingle(long userId, CancellationToken cancellationToken)
    {
        await using var scope = serviceFactory.CreateAsyncScope();
        var userService = scope.ServiceProvider.GetRequiredService<BotUserService>();
        var kuroSignService = scope.ServiceProvider.GetRequiredService<KuroSignService>();

        var user = await userService.GetByIdWithKuroAsync(userId, noTracking: true, cancellationToken);
        if (user == null)
            return;

        var kUser = user.KuroUser;
        if (kUser == null || kUser.Token.IsWhiteSpaceOrNull)
            return;

        var result = await kuroSignService.ExecuteAutoSignAsync(kUser, cancellationToken);
        if (result.HasResult)
        {
            var message = new StringBuilder("自动签到结果：\n");
            foreach (var line in result.Lines)
                message.AppendLine(line);
            message.AppendLine("时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await SendMessage(user.OwnerId, message.ToString(), cancellationToken);
        }
    }

    private async Task DoSigninAsync(CancellationToken cancellationToken)
    {
        try
        {
            var offset = 0;
            const int limit = 20;

            Dictionary<long, string> userIds;

            do
            {
                {
                    await using var scope = serviceFactory.CreateAsyncScope();
                    var userService = scope.ServiceProvider.GetRequiredService<BotUserService>();
                    userIds = await userService.GetAvailableUsersAsync(Software, offset, limit, cancellationToken);
                }

                if (userIds.IsEmpty)
                {
                    logger.LogInformation("No available users for kuro auto sign.");
                    return;
                }

                offset += limit;

                foreach (var (id, ownerId) in userIds)
                {
                    try
                    {
                        await ProcessSingle(id, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to process user {UserId} for kuro auto sign", id);
                        await SendMessage(ownerId, $"自动签到执行失败：{e.Message}", cancellationToken);
                    }
                }
            } while (userIds.Count == limit);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Kuro auto sign error occurred");
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

                logger.LogInformation("Next kuro auto sign execution at {ExecuteAt:yyyy/MM/dd HH:mm:ss} (in {Delay:g})", next, delay);
                await Task.Delay(delay, stoppingToken);

                await DoSigninAsync(stoppingToken);
                logger.LogInformation("Kuro auto sign task completed.");
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
