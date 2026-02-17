using System.Text;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Models.Common;
using OhMyLib.Requests.Kuro;
using OhMyLib.Requests.Kuro.Data;
using OhMyLib.Services;

namespace OhMyLib.HostedServices;

public abstract class KuroAutoSignService(ILogger logger, IServiceProvider provider) : BackgroundService
{
    private static readonly TimeSpan ExecuteAt = new(0, 10, 0);

    protected abstract SoftwareType Software { get; }

    protected abstract Task SendMessage(long chatId, string message, CancellationToken cancellationToken);

    private async Task ProcessSingle(BotUserService userService, BotUser user, CancellationToken cancellationToken)
    {
        var kUser = user.KuroUser;
        if (kUser == null || kUser.Token.IsWhiteSpaceOrNull)
            return;

        using var client = new KuroHttpClient(kUser.Token, kUser.DevCode, kUser.DistinctId, kUser.IpAddress);

        var taskProgress = await client.BbsGetTaskProgressAsync(kUser.BbsUserId ?? 0);
        if (!taskProgress.Success)
        {
            if (taskProgress.Code == 220)
            {
                kUser.Invalidate();
                await userService.SaveAsync(cancellationToken);

                throw new InvalidOperationException("用户Token已失效，请重新绑定库街区账号，自动签到将会关闭直到重新绑定。");
            }

            throw new InvalidOperationException("获取任务进度失败：" + taskProgress.Msg);
        }

        var currentProgress = taskProgress.Data?.DailyTask ?? [];
        if (currentProgress.IsEmpty)
            return;

        var tasks = kUser.BbsTask;
        var message = new StringBuilder("自动签到结果：\n");
        var hasResult = false;
        if (tasks != KuroBbsTaskType.None)
        {
            var signTask = currentProgress.FirstOrDefault(x => x.Remark == "用户签到");
            if ((tasks & KuroBbsTaskType.Signin) != 0)
            {
                hasResult = true;

                if (signTask?.Finished == true)
                {
                    message.AppendLine("社区签到结果：今日已签到");
                }
                else
                {
                    var signinResult = await client.BbsSignInAsync();
                    message.AppendLine($"社区签到结果：{(signinResult.Success ? "成功" : "失败")}");
                    if (!signinResult.Success)
                        message.AppendLine($"原因：{signinResult.Msg}");

                    await Task.Delay(Random.Shared.Next(1000, 2000), cancellationToken);
                }
            }

            KuroHttpResponse<KuroBbsPostData>? lazyPosts = null;

            var viewTask = currentProgress.FirstOrDefault(x => x.Remark == "浏览3篇帖子");
            if ((tasks & KuroBbsTaskType.ViewPosts) != 0)
            {
                hasResult = true;

                if (viewTask?.Finished == true)
                {
                    message.AppendLine("浏览帖子结果：任务已完成");
                }
                else
                {
                    lazyPosts ??= await client.BbsGetPostsAsync();
                    var posts = lazyPosts.Data?.PostList ?? [];

                    int succCnt = 0, failedCnt = 0;

                    for (var i = viewTask?.CompleteTimes ?? 0; i < viewTask?.NeedActionTimes; i++)
                    {
                        if (posts.IsEmpty)
                            break;

                        var post = posts[i % posts.Count];
                        var viewResult = await client.BbsGetPostDetailAsync(post.PostId);
                        if (viewResult.Success)
                            succCnt++;
                        else
                            failedCnt++;

                        await Task.Delay(Random.Shared.Next(1000, 2000), cancellationToken);
                    }

                    message.AppendLine($"浏览帖子结果：成功 {succCnt} 次，失败 {failedCnt} 次");
                }
            }

            var likeTask = currentProgress.FirstOrDefault(x => x.Remark == "点赞5次");
            if ((tasks & KuroBbsTaskType.LikePosts) != 0)
            {
                hasResult = true;

                if (likeTask?.Finished == true)
                {
                    message.AppendLine("点赞帖子结果：任务已完成");
                }
                else
                {
                    lazyPosts ??= await client.BbsGetPostsAsync();
                    var posts = lazyPosts.Data?.PostList ?? [];

                    int succCnt = 0, failedCnt = 0;

                    for (var i = likeTask?.CompleteTimes ?? 0; i < likeTask?.NeedActionTimes; i++)
                    {
                        if (posts.IsEmpty)
                            break;

                        var post = posts[i % posts.Count];
                        var likeResult = await client.BbsLikePostAsync(post.GameId,
                            post.GameForumId,
                            post.PostType,
                            post.PostId,
                            post.UserId);
                        if (likeResult.Success)
                            succCnt++;
                        else
                            failedCnt++;

                        await Task.Delay(Random.Shared.Next(2000, 5000), cancellationToken);
                    }

                    message.AppendLine($"点赞帖子结果：成功 {succCnt} 次，失败 {failedCnt} 次");
                }
            }

            var shareTask = currentProgress.FirstOrDefault(x => x.Remark == "分享1次帖子");
            if ((tasks & KuroBbsTaskType.SharePosts) != 0)
            {
                hasResult = true;

                if (shareTask?.Finished == true)
                {
                    message.AppendLine("分享帖子结果：任务已完成");
                }
                else
                {
                    var shareResult = await client.BbsSharePostAsync();
                    message.AppendLine($"分享帖子结果：{(shareResult.Success ? "成功" : "失败")}");
                    if (!shareResult.Success)
                        message.AppendLine($"原因：{shareResult.Msg}");

                    await Task.Delay(Random.Shared.Next(1000, 2000), cancellationToken);
                }
            }
        }

        foreach (var kGameConfig in kUser.GameConfigs
                     .Where(kGameConfig => kGameConfig.GameCharacterUid != 0)
                     .Where(kGameConfig => kGameConfig.TaskType != KuroGameTaskType.None)
                )
        {
            var init = await client.GameSignInInitAsync((int)kGameConfig.GameType, kGameConfig.GameType.ServerId, kGameConfig.GameCharacterUid,
                kUser.OwnerUserId);

            message.Append('[')
                .Append(kGameConfig.GameType.Name)
                .AppendLine("]");

            if (!init.Success || init.Data is not { } initData)
            {
                message.AppendLine($"初始化签到失败：{init.Msg}");
                continue;
            }

            hasResult = true;

            if (init.Data?.IsSigIn == false)
            {
                await Task.Delay(Random.Shared.Next(1000, 2000), cancellationToken);

                var sign = await client.GameSignInAsync((int)kGameConfig.GameType, kGameConfig.GameType.ServerId, kGameConfig.GameCharacterUid,
                    kUser.OwnerUserId);

                message.AppendLine($"签到结果：{(sign.Success ? "成功" : "失败")}");
                if (sign.Success)
                {
                    var current = initData.SigInNum + 1;
                    var items = GetSignInReward(current);
                    message.AppendLine("签到天数：" + current)
                        .AppendLine($"奖励：{items}");
                }
                else
                {
                    message.Append("原因：").AppendLine(sign.Msg);
                }
            }
            else
            {
                var current = initData.SigInNum;
                var items = GetSignInReward(current);
                message.AppendLine($"今日已签到，签到天数：{current}")
                    .AppendLine($"奖励：{items}");
            }

            await Task.Delay(Random.Shared.Next(1000, 2000), cancellationToken);
            continue;

            string GetSignInReward(int day)
            {
                return initData.SignInGoodsConfigs.Where(x => x.SerialNum == day - 1)
                    .Select(x => $"{x.GoodsName} x{x.GoodsNum}")
                    .JoinToString(", ");
            }
        }

        if (hasResult)
        {
            message.AppendLine("时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await SendMessage(long.Parse(user.OwnerId), message.ToString(), cancellationToken);
        }
    }

    private async Task DoSigninAsync(BotUserService userService, CancellationToken cancellationToken)
    {
        try
        {
            var offset = 0;
            const int limit = 20;

            var users = await userService.GetAvailableUsersAsync(Software, offset, limit, cancellationToken);
            if (users.IsEmpty)
            {
                logger.LogInformation("No available users for kuro auto sign.");
                return;
            }

            do
            {
                offset += limit;

                foreach (var user in users)
                {
                    try
                    {
                        await ProcessSingle(userService, user, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to process user {UserId} for kuro auto sign", user.Id);
                        await SendMessage(long.Parse(user.OwnerId), $"自动签到执行失败：{e.Message}", cancellationToken);
                    }
                }

                users = await userService.GetAvailableUsersAsync(Software, offset, limit, cancellationToken);
            } while (users.Count == limit);
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

                await using var scope = provider.CreateAsyncScope();
                await DoSigninAsync(scope.ServiceProvider.GetRequiredService<BotUserService>(), stoppingToken);
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