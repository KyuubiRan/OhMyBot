using System.Globalization;
using Microsoft.Extensions.Logging;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.Kuro;

public sealed class KuroSignService(
    KuroHttpClient client,
    KuroAccountService accountService,
    ILogger<KuroSignService> logger)
{
    public async Task<KuroBbsSignResult> ExecuteBbsSignAsync(
        KuroAccount account,
        long taskFlags,
        IReadOnlySet<string>? requestedActions = null,
        bool runAllWhenNoRequestedActions = false,
        CancellationToken cancellationToken = default)
    {
        var credential = accountService.GetCredential(account);
        var progressResponse = await client.GetTaskProgressAsync(credential, account.BbsUserId, cancellationToken: cancellationToken);
        await ThrowIfFatalResponseAsync(account, progressResponse, "获取任务进度失败：", cancellationToken);
        var currentProgress = progressResponse.Data?.DailyTask ?? [];
        var progress = currentProgress
            .Select(item => new KuroBbsTaskProgressSnapshot(item.Remark, item.CompleteTimes, item.NeedActionTimes, item.GainGold, item.Finished))
            .ToArray();
        var lines = new List<string>();
        KuroApiResponse<KuroPostListData>? lazyPosts = null;

        if (ShouldDoAction(taskFlags, KuroBbsTaskFlags.SignIn, requestedActions, "signin", runAllWhenNoRequestedActions))
        {
            var signTask = currentProgress.FirstOrDefault(item => item.Remark == "用户签到");
            if (signTask?.Finished == true)
            {
                lines.Add("社区签到结果：今日已签到");
            }
            else
            {
                var signResult = await client.BbsSignInAsync(credential, cancellationToken: cancellationToken);
                await ThrowIfTokenExpiredAsync(account, signResult, cancellationToken);
                lines.Add($"社区签到结果：{(signResult.Success ? "成功" : "失败")}");
                if (!signResult.Success)
                {
                    lines.Add("原因：" + signResult.Msg);
                }

                logger.Log(signResult.Success ? LogLevel.Information : LogLevel.Warning,
                    "Kuro BBS sign in finished for account {AccountId}, success={Success}, message={Message}",
                    account.Id,
                    signResult.Success,
                    signResult.Msg);
                await DelayAsync(1000, 2000, cancellationToken);
            }
        }

        if (ShouldDoAction(taskFlags, KuroBbsTaskFlags.ViewPosts, requestedActions, "view", runAllWhenNoRequestedActions))
        {
            var viewTask = currentProgress.FirstOrDefault(item => item.Remark == "浏览3篇帖子");
            if (viewTask?.Finished == true)
            {
                lines.Add("浏览帖子结果：任务已完成");
            }
            else
            {
                lazyPosts ??= await client.GetPostsAsync(credential, cancellationToken: cancellationToken);
                var posts = lazyPosts.Data?.PostList ?? [];
                var (success, failed) = await ExecutePostLoopAsync(
                    viewTask,
                    posts,
                    post => client.GetPostDetailAsync(credential, post.PostId, cancellationToken),
                    account,
                    cancellationToken);
                lines.Add($"浏览帖子结果：成功 {success} 次，失败 {failed} 次");
            }
        }

        if (ShouldDoAction(taskFlags, KuroBbsTaskFlags.LikePosts, requestedActions, "like", runAllWhenNoRequestedActions))
        {
            var likeTask = currentProgress.FirstOrDefault(item => item.Remark == "点赞5次");
            if (likeTask?.Finished == true)
            {
                lines.Add("点赞帖子结果：任务已完成");
            }
            else
            {
                lazyPosts ??= await client.GetPostsAsync(credential, cancellationToken: cancellationToken);
                var posts = lazyPosts.Data?.PostList ?? [];
                var (success, failed) = await ExecutePostLoopAsync(
                    likeTask,
                    posts,
                    post => client.LikePostAsync(credential, post.GameId, post.GameForumId, post.PostType, post.PostId, post.UserId, cancellationToken),
                    account,
                    cancellationToken,
                    delayMinMs: 2000,
                    delayMaxMs: 5000);
                lines.Add($"点赞帖子结果：成功 {success} 次，失败 {failed} 次");
            }
        }

        if (ShouldDoAction(taskFlags, KuroBbsTaskFlags.SharePosts, requestedActions, "share", runAllWhenNoRequestedActions))
        {
            var shareTask = currentProgress.FirstOrDefault(item => item.Remark == "分享1次帖子");
            if (shareTask?.Finished == true)
            {
                lines.Add("分享帖子结果：任务已完成");
            }
            else
            {
                var shareResult = await client.SharePostAsync(credential, cancellationToken: cancellationToken);
                await ThrowIfTokenExpiredAsync(account, shareResult, cancellationToken);
                lines.Add($"分享帖子结果：{(shareResult.Success ? "成功" : "失败")}");
                if (!shareResult.Success)
                {
                    lines.Add("原因：" + shareResult.Msg);
                }

                logger.Log(shareResult.Success ? LogLevel.Information : LogLevel.Warning,
                    "Kuro BBS share post finished for account {AccountId}, success={Success}, message={Message}",
                    account.Id,
                    shareResult.Success,
                    shareResult.Msg);
                await DelayAsync(1000, 2000, cancellationToken);
            }
        }

        return new KuroBbsSignResult(account, progress, lines);
    }

    public async Task<KuroGameSignResult> ExecuteGameSignAsync(
        KuroAccount account,
        IEnumerable<long>? requestedGames = null,
        bool onlyEnabledAutoSign = false,
        bool includeMissingConfigMessage = false,
        CancellationToken cancellationToken = default)
    {
        var credential = accountService.GetCredential(account);
        var lines = new List<string>();
        foreach (var role in ResolveGameTargets(account, requestedGames, onlyEnabledAutoSign, includeMissingConfigMessage, lines))
        {
            var init = await client.GameSignInInitAsync(credential, role.GameId, role.ServerId, role.RoleId, account.BbsUserId, cancellationToken);
            await ThrowIfTokenExpiredAsync(account, init, cancellationToken);
            lines.Add($"[{role.GameName}] {role.RoleName}");
            if (!init.Success || init.Data is not { } signData)
            {
                lines.Add("初始化签到失败：" + init.Msg);
                continue;
            }

            if (signData.IsSignIn)
            {
                lines.Add("签到结果：今日已签到");
                lines.Add("签到天数：" + signData.SignInNum.ToString(CultureInfo.InvariantCulture));
                lines.Add("奖励：" + GetSignInReward(signData, signData.SignInNum));
                continue;
            }

            await DelayAsync(1000, 3000, cancellationToken);
            var signResult = await client.GameSignInAsync(credential, role.GameId, role.ServerId, role.RoleId, account.BbsUserId, cancellationToken);
            await ThrowIfTokenExpiredAsync(account, signResult, cancellationToken);
            lines.Add($"签到结果：{(signResult.Success ? "成功" : "失败：" + signResult.Msg)}");
            lines.Add("签到天数：" + (signResult.Success ? signData.SignInNum + 1 : signData.SignInNum).ToString(CultureInfo.InvariantCulture));
            if (signResult.Success)
            {
                lines.Add("奖励：" + GetSignInReward(signData, signData.SignInNum + 1));
            }

            logger.Log(signResult.Success ? LogLevel.Information : LogLevel.Warning,
                "Kuro game sign finished for account {AccountId}, game={GameId}, role={RoleId}, success={Success}, message={Message}",
                account.Id,
                role.GameId,
                role.RoleId,
                signResult.Success,
                signResult.Msg);
            await DelayAsync(1000, 3000, cancellationToken);
        }

        return new KuroGameSignResult(account, lines);
    }

    public async Task<KuroAutoSignResult> ExecuteAutoSignAsync(KuroAccount account, CancellationToken cancellationToken = default)
    {
        var bbs = await ExecuteBbsSignAsync(account, account.BbsTaskFlags, runAllWhenNoRequestedActions: false, cancellationToken: cancellationToken);
        var game = await ExecuteGameSignAsync(account, onlyEnabledAutoSign: true, cancellationToken: cancellationToken);
        return new KuroAutoSignResult(account, bbs.Lines.Concat(game.Lines).ToArray());
    }

    private async Task<(int Success, int Failed)> ExecutePostLoopAsync<T>(
        KuroTaskProgressItem? task,
        IReadOnlyList<KuroPostItem> posts,
        Func<KuroPostItem, Task<KuroApiResponse<T>>> action,
        KuroAccount account,
        CancellationToken cancellationToken,
        int delayMinMs = 1000,
        int delayMaxMs = 2000)
    {
        var success = 0;
        var failed = 0;
        var start = task?.CompleteTimes ?? 0;
        var end = task?.NeedActionTimes ?? 0;
        for (var i = start; i < end; i++)
        {
            if (posts.Count == 0)
            {
                break;
            }

            var result = await action(posts[i % posts.Count]);
            if (result.Code == KuroHttpClient.TokenExpiredCode)
            {
                await accountService.ClearTokenAsync(account.Id, cancellationToken);
                throw new InvalidOperationException("Token 已失效，请重新绑定库街区账号");
            }

            if (result.Success)
            {
                success++;
            }
            else
            {
                failed++;
            }

            await DelayAsync(delayMinMs, delayMaxMs, cancellationToken);
        }

        return (success, failed);
    }

    private async Task ThrowIfFatalResponseAsync(
        KuroAccount account,
        KuroBaseResponse response,
        string messagePrefix,
        CancellationToken cancellationToken)
    {
        if (response.Success)
        {
            return;
        }

        if (response.Code == KuroHttpClient.TokenExpiredCode)
        {
            await accountService.ClearTokenAsync(account.Id, cancellationToken);
            throw new InvalidOperationException("Token 已失效，请重新绑定库街区账号");
        }

        throw new InvalidOperationException(messagePrefix + response.Msg);
    }

    private async Task ThrowIfTokenExpiredAsync(KuroAccount account, KuroBaseResponse response, CancellationToken cancellationToken)
    {
        if (response.Code != KuroHttpClient.TokenExpiredCode)
        {
            return;
        }

        await accountService.ClearTokenAsync(account.Id, cancellationToken);
        throw new InvalidOperationException("Token 已失效，请重新绑定库街区账号");
    }

    private static bool ShouldDoAction(
        long configuredTasks,
        long target,
        IReadOnlySet<string>? requestedActions,
        string key,
        bool runAllWhenNoRequestedActions)
    {
        return (configuredTasks & target) != 0
               || (runAllWhenNoRequestedActions && (requestedActions == null || requestedActions.Count == 0))
               || (requestedActions?.Contains(key) ?? false);
    }

    private static IEnumerable<KuroGameRole> ResolveGameTargets(
        KuroAccount account,
        IEnumerable<long>? requestedGames,
        bool onlyEnabledAutoSign,
        bool includeMissingConfigMessage,
        ICollection<string> lines)
    {
        var requestedList = requestedGames?.Distinct().ToArray();
        if (requestedList is { Length: > 0 })
        {
            foreach (var gameId in requestedList)
            {
                var role = account.Roles.FirstOrDefault(item => item.GameId == gameId);
                if (role is null)
                {
                    if (includeMissingConfigMessage)
                    {
                        lines.Add($"未找到 {KuroGameNames.Format(gameId)} 角色信息，请使用 /kuro game init {account.Id} 同步角色");
                    }

                    continue;
                }

                if (onlyEnabledAutoSign && !role.AutoSignEnabled)
                {
                    continue;
                }

                yield return role;
            }

            yield break;
        }

        foreach (var role in account.Roles)
        {
            if (onlyEnabledAutoSign && !role.AutoSignEnabled)
            {
                continue;
            }

            yield return role;
        }
    }

    private static string GetSignInReward(KuroGameSignInInitData signData, int day)
    {
        return string.Join(", ", signData.SignInGoodsConfigs
            .Where(item => item.SerialNum == day - 1)
            .Select(item => $"{item.GoodsName} x{item.GoodsNum}"));
    }

    private static Task DelayAsync(int minMs, int maxMs, CancellationToken cancellationToken)
    {
        return Task.Delay(Random.Shared.Next(minMs, maxMs), cancellationToken);
    }
}

public sealed record KuroBbsTaskProgressSnapshot(string Remark, int CompleteTimes, int NeedActionTimes, int GainGold, bool Finished);

public sealed record KuroBbsSignResult(KuroAccount Account, IReadOnlyList<KuroBbsTaskProgressSnapshot> Progress, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record KuroGameSignResult(KuroAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record KuroAutoSignResult(KuroAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed class KuroTokenExpiredException(long accountId) : Exception("Token 已失效，请重新绑定库街区账号")
{
    public long AccountId { get; } = accountId;
}
