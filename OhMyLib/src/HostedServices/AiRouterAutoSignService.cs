using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.Services.AiRouter;

namespace OhMyLib.HostedServices;

public abstract class AiRouterAutoSignService(ILogger logger, IServiceScopeFactory serviceFactory) : BackgroundService
{
    private static readonly TimeSpan ExecuteAt = new(0, 15, 0);

    protected abstract Task SendMessage(string chatId, string message, CancellationToken cancellationToken);

    protected virtual string BuildSignMessage(AiRouterSignResult result, DateTimeOffset time)
    {
        var message = new StringBuilder("[AI Router-自动签到]\n");
        message.AppendLine(result.ToMessage());
        message.AppendLine("时间：" + time.ToString("yyyy-MM-dd HH:mm:ss"));
        return message.ToString();
    }

    protected virtual string BuildFailureMessage(Exception exception) =>
        $"[AI Router-自动签到]\n自动签到执行失败：{exception.GetBaseException().Message}";

    private async Task ProcessSingle(long accountId, CancellationToken cancellationToken)
    {
        await using var scope = serviceFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<AiRouterSignService>();

        var account = await accountService.FindByIdAsync(accountId, noTracking: true, cancellationToken);
        if (account == null)
            return;

        var result = await signService.SignInAsync(account, cancellationToken);
        await SendMessage(account.OwnerBotUser.OwnerId, BuildSignMessage(result, DateTimeOffset.Now), cancellationToken);
    }

    private async Task DoSignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            var offset = 0;
            const int limit = 20;
            List<(long AccountId, string OwnerId)> targets;

            do
            {
                await using (var scope = serviceFactory.CreateAsyncScope())
                {
                    var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
                    var accounts = await accountService.ListAutoSignTargetsAsync(offset, limit, cancellationToken);
                    targets = accounts.Select(x => (x.Id, x.OwnerBotUser.OwnerId)).ToList();
                }

                if (targets.Count == 0)
                {
                    logger.LogInformation("No available users for AI Router auto sign.");
                    return;
                }

                offset += limit;

                foreach (var (accountId, ownerId) in targets)
                {
                    try
                    {
                        await ProcessSingle(accountId, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to process AI Router account {AccountId} for auto sign", accountId);
                        await SendMessage(ownerId, BuildFailureMessage(e), cancellationToken);
                    }
                }
            } while (targets.Count == limit);
        }
        catch (Exception e)
        {
            logger.LogError(e, "AI Router auto sign error occurred");
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

                logger.LogInformation("Next AI Router auto sign execution at {ExecuteAt:yyyy/MM/dd HH:mm:ss} (in {Delay:g})", next, delay);
                await Task.Delay(delay, stoppingToken);

                await DoSignInAsync(stoppingToken);
                logger.LogInformation("AI Router auto sign task completed.");
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "AI Router daily job failed");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
