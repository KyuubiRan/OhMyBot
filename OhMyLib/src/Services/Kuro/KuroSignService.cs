using FoxTail.Extensions;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums.Kuro;
using OhMyLib.Models.Kuro;
using OhMyLib.Requests.Kuro;
using OhMyLib.Requests.Kuro.Data;

namespace OhMyLib.Services.Kuro;

[Component]
public class KuroSignService(KuroUserService kuroUserService, ILogger<KuroSignService> logger)
{
    public async Task<KuroBbsSignResult> ExecuteBbsSignAsync(
        KuroUser kuroUser,
        KuroBbsTaskType tasks,
        IEnumerable<string>? requestedActions = null,
        bool runAllWhenNoRequestedActions = false,
        Func<IReadOnlyList<KuroBbsTaskProgressSnapshot>, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSignable(kuroUser, requireBbsUserId: true);

        using var client = new KuroHttpClient(kuroUser);

        var taskProgress = await client.BbsGetTaskProgressAsync(kuroUser.BbsUserId!.Value);
        await ThrowIfFatalResponseAsync(kuroUser, taskProgress, "获取任务进度失败：", cancellationToken);

        var currentProgress = taskProgress.Data?.DailyTask ?? [];
        var progress = currentProgress.Select(x => new KuroBbsTaskProgressSnapshot(x.Remark, x.CompleteTimes, x.NeedActionTimes, x.GainGold, x.Finished))
                                     .ToList();

        if (onProgress != null)
            await onProgress(progress, cancellationToken);

        var resultLines = new List<string>();
        var actionSet = requestedActions?.Where(x => !x.IsWhiteSpaceOrNull)
                                         .Select(x => x.Trim())
                                         .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        KuroHttpResponse<KuroBbsPostData>? lazyPosts = null;

        if (ShouldDoAction(tasks, KuroBbsTaskType.Signin, actionSet, "signin", runAllWhenNoRequestedActions))
        {
            var signTask = currentProgress.FirstOrDefault(x => x.Remark == "用户签到");
            if (signTask?.Finished == true)
            {
                resultLines.Add("社区签到结果：今日已签到");
            }
            else
            {
                var signResult = await client.BbsSignInAsync();
                await ThrowIfTokenExpiredAsync(kuroUser, signResult, cancellationToken);
                resultLines.Add($"社区签到结果：{(signResult.Success ? "成功" : "失败")}");
                if (!signResult.Success)
                    resultLines.Add("原因：" + signResult.Msg);

                logger.Log(signResult.Success ? LogLevel.Information : LogLevel.Warning,
                           "Kuro BBS sign in finished for user {UserId}, success={Success}, message={Message}",
                           kuroUser.OwnerUserId,
                           signResult.Success,
                           signResult.Msg);

                await DelayAsync(1000, 2000, cancellationToken);
            }
        }

        if (ShouldDoAction(tasks, KuroBbsTaskType.ViewPosts, actionSet, "view", runAllWhenNoRequestedActions))
        {
            var viewTask = currentProgress.FirstOrDefault(x => x.Remark == "浏览3篇帖子");
            if (viewTask?.Finished == true)
            {
                resultLines.Add("浏览帖子结果：任务已完成");
            }
            else
            {
                lazyPosts ??= await client.BbsGetPostsAsync();
                var posts = lazyPosts.Data?.PostList ?? [];

                int succCnt = 0, failedCnt = 0;
                var start = viewTask?.CompleteTimes ?? 0;
                var end = viewTask?.NeedActionTimes ?? 0;

                for (var i = start; i < end; i++)
                {
                    if (posts.IsEmpty)
                        break;

                    var post = posts[i % posts.Count];
                    var viewResult = await client.BbsGetPostDetailAsync(post.PostId);
                    await ThrowIfTokenExpiredAsync(kuroUser, viewResult, cancellationToken);
                    if (viewResult.Success)
                        succCnt++;
                    else
                        failedCnt++;

                    logger.Log(viewResult.Success ? LogLevel.Information : LogLevel.Warning,
                               "Kuro BBS view post finished for user {UserId}, post={PostId}, success={Success}, message={Message}",
                               kuroUser.OwnerUserId,
                               post.PostId,
                               viewResult.Success,
                               viewResult.Msg);

                    await DelayAsync(1000, 2000, cancellationToken);
                }

                resultLines.Add($"浏览帖子结果：成功 {succCnt} 次，失败 {failedCnt} 次");
            }
        }

        if (ShouldDoAction(tasks, KuroBbsTaskType.LikePosts, actionSet, "like", runAllWhenNoRequestedActions))
        {
            var likeTask = currentProgress.FirstOrDefault(x => x.Remark == "点赞5次");
            if (likeTask?.Finished == true)
            {
                resultLines.Add("点赞帖子结果：任务已完成");
            }
            else
            {
                lazyPosts ??= await client.BbsGetPostsAsync();
                var posts = lazyPosts.Data?.PostList ?? [];

                int succCnt = 0, failedCnt = 0;
                var start = likeTask?.CompleteTimes ?? 0;
                var end = likeTask?.NeedActionTimes ?? 0;

                for (var i = start; i < end; i++)
                {
                    if (posts.IsEmpty)
                        break;

                    var post = posts[i % posts.Count];
                    var likeResult = await client.BbsLikePostAsync(post.GameId, post.GameForumId, post.PostType, post.PostId, post.UserId);
                    await ThrowIfTokenExpiredAsync(kuroUser, likeResult, cancellationToken);
                    if (likeResult.Success)
                        succCnt++;
                    else
                        failedCnt++;

                    logger.Log(likeResult.Success ? LogLevel.Information : LogLevel.Warning,
                               "Kuro BBS like post finished for user {UserId}, post={PostId}, success={Success}, message={Message}",
                               kuroUser.OwnerUserId,
                               post.PostId,
                               likeResult.Success,
                               likeResult.Msg);

                    await DelayAsync(2000, 5000, cancellationToken);
                }

                resultLines.Add($"点赞帖子结果：成功 {succCnt} 次，失败 {failedCnt} 次");
            }
        }

        if (ShouldDoAction(tasks, KuroBbsTaskType.SharePosts, actionSet, "share", runAllWhenNoRequestedActions))
        {
            var shareTask = currentProgress.FirstOrDefault(x => x.Remark == "分享1次帖子");
            if (shareTask?.Finished == true)
            {
                resultLines.Add("分享帖子结果：任务已完成");
            }
            else
            {
                var shareResult = await client.BbsSharePostAsync();
                await ThrowIfTokenExpiredAsync(kuroUser, shareResult, cancellationToken);
                resultLines.Add($"分享帖子结果：{(shareResult.Success ? "成功" : "失败")}");
                if (!shareResult.Success)
                    resultLines.Add("原因：" + shareResult.Msg);

                logger.Log(shareResult.Success ? LogLevel.Information : LogLevel.Warning,
                           "Kuro BBS share post finished for user {UserId}, success={Success}, message={Message}",
                           kuroUser.OwnerUserId,
                           shareResult.Success,
                           shareResult.Msg);

                await DelayAsync(1000, 2000, cancellationToken);
            }
        }

        return new KuroBbsSignResult(progress, resultLines);
    }

    public async Task<KuroGameSignResult> ExecuteGameSignAsync(
        KuroUser kuroUser,
        IEnumerable<KuroGameType>? requestedGames = null,
        bool onlyEnabledAutoSign = false,
        bool includeMissingConfigMessage = false,
        CancellationToken cancellationToken = default)
    {
        EnsureSignable(kuroUser, requireBbsUserId: true);

        var resultLines = new List<string>();
        using var client = new KuroHttpClient(kuroUser);

        foreach (var target in ResolveGameTargets(kuroUser, requestedGames, onlyEnabledAutoSign, includeMissingConfigMessage, resultLines))
        {
            var gameType = target.GameType;
            var init = await client.GameSignInInitAsync((int)gameType, gameType.ServerId, target.GameCharacterUid, kuroUser.BbsUserId!.Value);
            await ThrowIfTokenExpiredAsync(kuroUser, init, cancellationToken);

            resultLines.Add($"[{gameType.Name}]");

            if (!init.Success || init.Data is not { } signData)
            {
                resultLines.Add("初始化签到失败：" + init.Msg);
                continue;
            }

            if (signData.IsSigIn)
            {
                resultLines.Add("签到结果：今日已签到");
                resultLines.Add("签到天数：" + signData.SigInNum);
                resultLines.Add("奖励：" + GetSignInReward(signData, signData.SigInNum));
                continue;
            }

            await DelayAsync(1000, 3000, cancellationToken);

            var signInResult = await client.GameSignInAsync((int)gameType, gameType.ServerId, target.GameCharacterUid, kuroUser.BbsUserId.Value);
            await ThrowIfTokenExpiredAsync(kuroUser, signInResult, cancellationToken);
            resultLines.Add($"签到结果：{(signInResult.Success ? "成功" : "失败：" + signInResult.Msg)}");
            resultLines.Add("签到天数：" + (signInResult.Success ? signData.SigInNum + 1 : signData.SigInNum));

            if (signInResult.Success)
                resultLines.Add("奖励：" + GetSignInReward(signData, signData.SigInNum + 1));

            logger.Log(signInResult.Success ? LogLevel.Information : LogLevel.Warning,
                       "Kuro game sign finished for user {UserId}, game={GameType}, success={Success}, message={Message}",
                       kuroUser.OwnerUserId,
                       gameType,
                       signInResult.Success,
                       signInResult.Msg);

            await DelayAsync(1000, 3000, cancellationToken);
        }

        return new KuroGameSignResult(resultLines);
    }

    public async Task<KuroAutoSignResult> ExecuteAutoSignAsync(KuroUser kuroUser, CancellationToken cancellationToken = default)
    {
        var resultLines = new List<string>();

        var bbsResult = await ExecuteBbsSignAsync(kuroUser, kuroUser.BbsTask, runAllWhenNoRequestedActions: false, cancellationToken: cancellationToken);
        resultLines.AddRange(bbsResult.Lines);

        var gameResult = await ExecuteGameSignAsync(kuroUser, onlyEnabledAutoSign: true, cancellationToken: cancellationToken);
        resultLines.AddRange(gameResult.Lines);

        return new KuroAutoSignResult(resultLines);
    }

    private static bool ShouldDoAction(
        KuroBbsTaskType configuredTasks,
        KuroBbsTaskType target,
        IReadOnlySet<string>? requestedActions,
        string key,
        bool runAllWhenNoRequestedActions)
    {
        return (configuredTasks & target) != 0
               || (runAllWhenNoRequestedActions && (requestedActions == null || requestedActions.Count == 0))
               || (requestedActions?.Contains(key) ?? false);
    }

    private static IEnumerable<KuroGameConfig> ResolveGameTargets(
        KuroUser kuroUser,
        IEnumerable<KuroGameType>? requestedGames,
        bool onlyEnabledAutoSign,
        bool includeMissingConfigMessage,
        ICollection<string> resultLines)
    {
        var requestedList = requestedGames?.Distinct().ToList();
        if (requestedList is { Count: > 0 })
        {
            foreach (var gameType in requestedList)
            {
                var config = kuroUser.GameConfigs.FirstOrDefault(x => x.GameType == gameType);
                if (config == null || config.GameCharacterUid == 0)
                {
                    if (includeMissingConfigMessage)
                        resultLines.Add($"未找到 {gameType.Name} 角色信息，请使用 /kuro_game_init_char 初始化游戏角色信息");
                    continue;
                }

                if (onlyEnabledAutoSign && (config.TaskType & KuroGameTaskType.Signin) == 0)
                    continue;

                yield return config;
            }

            yield break;
        }

        foreach (var config in kuroUser.GameConfigs.Where(x => x.GameCharacterUid != 0))
        {
            if (onlyEnabledAutoSign && (config.TaskType & KuroGameTaskType.Signin) == 0)
                continue;

            yield return config;
        }
    }

    private async Task ThrowIfFatalResponseAsync(
        KuroUser kuroUser,
        KuroBaseHttpResponse response,
        string messagePrefix,
        CancellationToken cancellationToken)
    {
        if (response.Success)
            return;

        if (response.Code == 220)
        {
            await kuroUserService.InvalidateAsync(kuroUser.Id, cancellationToken);
            throw new InvalidOperationException("Token已失效，请重新绑定库街区账号后再使用签到功能");
        }

        throw new InvalidOperationException(messagePrefix + response.Msg);
    }

    private async Task ThrowIfTokenExpiredAsync(KuroUser kuroUser, KuroBaseHttpResponse response, CancellationToken cancellationToken)
    {
        if (response.Code == 220)
        {
            await kuroUserService.InvalidateAsync(kuroUser.Id, cancellationToken);
            throw new InvalidOperationException("Token已失效，请重新绑定库街区账号后再使用签到功能");
        }
    }

    private static string GetSignInReward(KuroSignInInitData signData, int day)
    {
        return signData.SignInGoodsConfigs.Where(x => x.SerialNum == day - 1)
                       .Select(x => $"{x.GoodsName} x{x.GoodsNum}")
                       .JoinToString(", ");
    }

    private static async Task DelayAsync(int minMs, int maxMs, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(minMs, maxMs), cancellationToken);
    }

    private static void EnsureSignable(KuroUser kuroUser, bool requireBbsUserId)
    {
        ArgumentNullException.ThrowIfNull(kuroUser);

        if (kuroUser.Token.IsWhiteSpaceOrNull)
            throw new InvalidOperationException("绑定的库街区账号信息不完整，请重新绑定后再使用签到功能");

        if (requireBbsUserId && kuroUser.BbsUserId == null)
            throw new InvalidOperationException("请先绑定库街区账号并初始化用户信息后再使用签到功能");
    }
}

public sealed record KuroBbsTaskProgressSnapshot(string Remark, int CompleteTimes, int NeedActionTimes, int GainGold, bool Finished);

public sealed record KuroBbsSignResult(IReadOnlyList<KuroBbsTaskProgressSnapshot> Progress, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record KuroGameSignResult(IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record KuroAutoSignResult(IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}
