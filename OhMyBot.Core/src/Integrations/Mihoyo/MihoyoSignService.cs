using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed class MihoyoSignService(
    MihoyoHttpClient client,
    MihoyoAccountService accountService,
    IOptions<MihoyoOptions> optionsAccessor,
    ILogger<MihoyoSignService> logger)
{
    private readonly MihoyoOptions _options = optionsAccessor.Value;

    // ---------------- 米游社任务（仅 CN）----------------

    public async Task<MihoyoBbsSignResult> ExecuteBbsSignAsync(
        MihoyoAccount account,
        long taskFlags,
        IReadOnlySet<string>? requestedActions = null,
        bool runAllWhenNoRequestedActions = false,
        CancellationToken cancellationToken = default)
    {
        if (account.Region == MihoyoRegion.Os)
        {
            return new MihoyoBbsSignResult(account, ["国际服(HoYoLAB) 无米游社任务，已跳过"]);
        }

        var credential = accountService.GetCredential(account);
        if (string.IsNullOrEmpty(credential.Stoken))
        {
            return new MihoyoBbsSignResult(account, ["未提供 stoken，无法执行米游社任务（绑定时请使用包含 stoken 的 Cookie）"]);
        }

        var cookie = credential.Cookie;
        var missions = await client.GetMissionsAsync(cookie, cancellationToken);
        if (missions.Retcode == MihoyoHttpClient.CookieExpiredCode)
        {
            cookie = await RefreshCnCookieAsync(account, credential, cancellationToken);
            missions = await client.GetMissionsAsync(cookie, cancellationToken);
        }

        var lines = new List<string>();
        var signDone = false;
        var readDone = false;
        var likeDone = false;
        var shareDone = false;
        var readRemaining = 3;
        var likeRemaining = 5;
        if (missions.Ok && missions.Data is { } data)
        {
            if (data.CanGetPoints == 0)
            {
                signDone = readDone = likeDone = shareDone = true;
            }
            else
            {
                foreach (var state in data.States)
                {
                    switch (state.MissionId)
                    {
                        case 58 when state.IsGetAward: signDone = true; break;
                        case 59: readDone = state.IsGetAward; readRemaining -= state.HappenedTimes; break;
                        case 60: likeDone = state.IsGetAward; likeRemaining -= state.HappenedTimes; break;
                        case 61 when state.IsGetAward: shareDone = true; break;
                    }
                }
            }
        }

        var deviceId = credential.DeviceId;
        IReadOnlyList<MihoyoPostInfo>? posts = null;

        if (ShouldDoAction(taskFlags, MihoyoBbsTaskFlags.SignIn, requestedActions, "signin", runAllWhenNoRequestedActions))
        {
            if (signDone)
            {
                lines.Add("社区签到：今日已完成");
            }
            else
            {
                var signResult = await client.BbsSignInAsync(credential.StokenCookie, deviceId, GidsForSign(), cancellationToken);
                await ThrowIfStokenDeadAsync(account, signResult.Retcode, cancellationToken);
                if (signResult.Retcode == 1034)
                {
                    lines.Add("社区签到：触发验证码，已跳过（不支持自动打码）");
                }
                else
                {
                    lines.Add($"社区签到：{(signResult.Ok ? "成功" : "失败：" + signResult.Message)}");
                }

                await DelayAsync(2000, 4000, cancellationToken);
            }
        }

        if (ShouldDoAction(taskFlags, MihoyoBbsTaskFlags.ViewPosts, requestedActions, "view", runAllWhenNoRequestedActions))
        {
            if (readDone || readRemaining <= 0)
            {
                lines.Add("浏览帖子：任务已完成");
            }
            else
            {
                posts ??= await LoadPostsAsync(credential, deviceId, cancellationToken);
                var success = 0;
                for (var i = 0; i < readRemaining && posts.Count > 0; i++)
                {
                    var result = await client.ReadPostAsync(credential.StokenCookie, deviceId, posts[i % posts.Count].PostId, cancellationToken);
                    await ThrowIfStokenDeadAsync(account, result.Retcode, cancellationToken);
                    if (result.Ok)
                    {
                        success++;
                    }

                    await DelayAsync(2000, 4000, cancellationToken);
                }

                lines.Add($"浏览帖子：成功 {success} 次");
            }
        }

        if (ShouldDoAction(taskFlags, MihoyoBbsTaskFlags.LikePosts, requestedActions, "like", runAllWhenNoRequestedActions))
        {
            if (likeDone || likeRemaining <= 0)
            {
                lines.Add("点赞帖子：任务已完成");
            }
            else
            {
                posts ??= await LoadPostsAsync(credential, deviceId, cancellationToken);
                var success = 0;
                var captcha = false;
                for (var i = 0; i < likeRemaining && posts.Count > 0; i++)
                {
                    var result = await client.LikePostAsync(credential.StokenCookie, deviceId, posts[i % posts.Count].PostId, cancellationToken);
                    await ThrowIfStokenDeadAsync(account, result.Retcode, cancellationToken);
                    if (result.Retcode == 1034)
                    {
                        captcha = true;
                        break;
                    }

                    if (result.Ok)
                    {
                        success++;
                    }

                    await DelayAsync(2000, 4000, cancellationToken);
                }

                lines.Add(captcha
                    ? $"点赞帖子：成功 {success} 次后触发验证码，已跳过（不支持自动打码）"
                    : $"点赞帖子：成功 {success} 次");
            }
        }

        if (ShouldDoAction(taskFlags, MihoyoBbsTaskFlags.SharePosts, requestedActions, "share", runAllWhenNoRequestedActions))
        {
            if (shareDone)
            {
                lines.Add("分享帖子：任务已完成");
            }
            else
            {
                posts ??= await LoadPostsAsync(credential, deviceId, cancellationToken);
                if (posts.Count == 0)
                {
                    lines.Add("分享帖子：无可用帖子");
                }
                else
                {
                    var result = await client.SharePostAsync(credential.StokenCookie, deviceId, posts[0].PostId, cancellationToken);
                    await ThrowIfStokenDeadAsync(account, result.Retcode, cancellationToken);
                    lines.Add($"分享帖子：{(result.Ok ? "成功" : "失败：" + result.Message)}");
                }
            }
        }

        return new MihoyoBbsSignResult(account, lines);
    }

    // ---------------- 游戏签到 ----------------

    public async Task<MihoyoGameSignResult> ExecuteGameSignAsync(
        MihoyoAccount account,
        IEnumerable<string>? requestedGameKeys = null,
        bool onlyEnabledAutoSign = false,
        bool includeMissingConfigMessage = false,
        CancellationToken cancellationToken = default)
    {
        var credential = accountService.GetCredential(account);
        var lines = new List<string>();
        var roles = ResolveGameTargets(account, requestedGameKeys, onlyEnabledAutoSign, includeMissingConfigMessage, lines);
        var cookie = credential.Cookie;

        foreach (var role in roles)
        {
            var game = MihoyoGameCatalog.FindByGameBiz(role.GameBiz);
            if (game is null)
            {
                continue;
            }

            lines.Add($"[{role.GameName}]{(role.GameUid > 0 ? " " + role.Nickname + " (" + role.GameUid + ")" : string.Empty)}");
            try
            {
                if (account.Region == MihoyoRegion.Cn)
                {
                    cookie = await SignCnGameAsync(account, credential, cookie, game, role, lines, cancellationToken);
                }
                else
                {
                    await SignOsGameAsync(credential, game, lines, cancellationToken);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Mihoyo game sign failed for account {AccountId}, game {Game}.", account.Id, role.GameBiz);
                lines.Add("签到失败：" + exception.GetBaseException().Message);
            }

            await DelayAsync(2000, 5000, cancellationToken);
        }

        return new MihoyoGameSignResult(account, lines);
    }

    public async Task<MihoyoAutoSignResult> ExecuteAutoSignAsync(MihoyoAccount account, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        if (account.Region == MihoyoRegion.Cn)
        {
            var bbs = await ExecuteBbsSignAsync(account, account.BbsTaskFlags, runAllWhenNoRequestedActions: false, cancellationToken: cancellationToken);
            lines.AddRange(bbs.Lines);
        }

        var game = await ExecuteGameSignAsync(account, onlyEnabledAutoSign: true, cancellationToken: cancellationToken);
        lines.AddRange(game.Lines);
        return new MihoyoAutoSignResult(account, lines);
    }

    private async Task<string> SignCnGameAsync(
        MihoyoAccount account,
        MihoyoCredential credential,
        string cookie,
        MihoyoGameDef game,
        MihoyoGameRole role,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        var uid = role.GameUid.ToString(CultureInfo.InvariantCulture);
        var info = await client.GetCnGameInfoAsync(cookie, game, role.Region, uid, cancellationToken);
        if (info.Retcode == MihoyoHttpClient.CookieExpiredCode)
        {
            cookie = await RefreshCnCookieAsync(account, credential, cancellationToken);
            info = await client.GetCnGameInfoAsync(cookie, game, role.Region, uid, cancellationToken);
        }

        if (!info.Ok || info.Data is not { } infoData)
        {
            lines.Add("获取签到信息失败：" + info.Message);
            return cookie;
        }

        if (infoData.FirstBind)
        {
            lines.Add("首次绑定米游社，请先手动签到一次");
            return cookie;
        }

        var home = await client.GetCnGameHomeAsync(cookie, game, cancellationToken);
        var awards = home.Data?.Awards ?? [];

        if (infoData.IsSign)
        {
            lines.Add("签到结果：今日已签到");
            lines.Add($"连续签到：{infoData.TotalSignDay} 天");
            lines.Add("今日奖励：" + GetAward(awards, infoData.TotalSignDay - 1));
            return cookie;
        }

        await DelayAsync(2000, 5000, cancellationToken);
        var sign = await client.CnGameSignAsync(cookie, game, role.Region, uid, cancellationToken);
        if (sign.Retcode == MihoyoHttpClient.AlreadySignedCode)
        {
            lines.Add("签到结果：今日已签到");
            lines.Add($"连续签到：{infoData.TotalSignDay} 天");
            return cookie;
        }

        if (sign.Ok && sign.Data is { Success: 1 })
        {
            lines.Add("签到结果：触发验证码，已跳过（不支持自动打码）");
            return cookie;
        }

        if (!sign.Ok)
        {
            lines.Add("签到结果：失败：" + sign.Message);
            return cookie;
        }

        lines.Add("签到结果：成功");
        lines.Add($"连续签到：{infoData.TotalSignDay + 1} 天");
        lines.Add("今日奖励：" + GetAward(awards, infoData.TotalSignDay));
        logger.LogInformation("Mihoyo CN game sign success for account {AccountId}, game {Game}.", account.Id, game.Key);
        return cookie;
    }

    private async Task SignOsGameAsync(
        MihoyoCredential credential,
        MihoyoGameDef game,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        var info = await client.GetOsGameInfoAsync(credential.Cookie, game, cancellationToken);
        if (!info.Ok || info.Data is not { } infoData)
        {
            lines.Add("获取签到信息失败：" + info.Message);
            return;
        }

        if (infoData.FirstBind)
        {
            lines.Add("首次绑定，请先在 HoYoLAB 手动签到一次");
            return;
        }

        var home = await client.GetOsGameHomeAsync(credential.Cookie, game, cancellationToken);
        var awards = home.Data?.Awards ?? [];

        if (infoData.IsSign)
        {
            lines.Add("签到结果：今日已签到");
            lines.Add($"连续签到：{infoData.TotalSignDay} 天");
            lines.Add("今日奖励：" + GetAward(awards, infoData.TotalSignDay - 1));
            return;
        }

        await DelayAsync(2000, 6000, cancellationToken);
        var sign = await client.OsGameSignAsync(credential.Cookie, game, cancellationToken);
        if (sign.Retcode == MihoyoHttpClient.AlreadySignedCode)
        {
            lines.Add("签到结果：今日已签到");
            return;
        }

        if (!sign.Ok)
        {
            lines.Add("签到结果：失败：" + sign.Message);
            return;
        }

        lines.Add("签到结果：成功");
        lines.Add($"连续签到：{infoData.TotalSignDay + 1} 天");
        lines.Add("今日奖励：" + GetAward(awards, infoData.TotalSignDay));
    }

    private async Task<IReadOnlyList<MihoyoPostInfo>> LoadPostsAsync(MihoyoCredential credential, string deviceId, CancellationToken cancellationToken)
    {
        var response = await client.GetPostListAsync(credential.StokenCookie, deviceId, BbsForumId(), cancellationToken);
        return response.Data?.List
            .Select(wrapper => wrapper.Post)
            .Where(post => !string.IsNullOrEmpty(post.PostId))
            .ToArray() ?? [];
    }

    private async Task<string> RefreshCnCookieAsync(MihoyoAccount account, MihoyoCredential credential, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credential.Stoken))
        {
            await accountService.ClearCredentialAsync(account.Id, cancellationToken);
            throw new InvalidOperationException("Cookie 已失效且缺少 stoken，请重新绑定米游社账号");
        }

        var response = await client.RefreshCookieTokenAsync(credential.StokenCookie, cancellationToken);
        if (!response.Ok || string.IsNullOrEmpty(response.Data?.CookieToken))
        {
            await accountService.ClearCredentialAsync(account.Id, cancellationToken);
            throw new InvalidOperationException("stoken 已失效，请重新绑定米游社账号");
        }

        var cookie = MihoyoAccountService.SetCookieToken(credential.Cookie, response.Data.CookieToken);
        await accountService.UpdateCookieAsync(account.Id, cookie, cancellationToken);
        return cookie;
    }

    private async Task ThrowIfStokenDeadAsync(MihoyoAccount account, int retcode, CancellationToken cancellationToken)
    {
        if (retcode != MihoyoHttpClient.CookieExpiredCode)
        {
            return;
        }

        await accountService.ClearCredentialAsync(account.Id, cancellationToken);
        throw new InvalidOperationException("Cookie/stoken 已失效，请重新绑定米游社账号");
    }

    private IEnumerable<MihoyoGameRole> ResolveGameTargets(
        MihoyoAccount account,
        IEnumerable<string>? requestedGameKeys,
        bool onlyEnabledAutoSign,
        bool includeMissingConfigMessage,
        ICollection<string> lines)
    {
        var requested = requestedGameKeys?
            .Select(key => MihoyoGameCatalog.FindByKey(key)?.CnGameBiz)
            .Where(biz => biz is not null)
            .Select(biz => biz!)
            .Distinct()
            .ToArray();

        if (requested is { Length: > 0 })
        {
            foreach (var gameBiz in requested)
            {
                var roles = account.Roles.Where(role => role.GameBiz == gameBiz).ToArray();
                if (roles.Length == 0)
                {
                    if (includeMissingConfigMessage)
                    {
                        lines.Add($"未找到 {MihoyoGameCatalog.NameForGameBiz(gameBiz)} 角色，请使用 /mihoyo game init {account.Id} 同步");
                    }

                    continue;
                }

                foreach (var role in roles)
                {
                    if (onlyEnabledAutoSign && !role.AutoSignEnabled)
                    {
                        continue;
                    }

                    yield return role;
                }
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

    private string GidsForSign()
    {
        return _options.BbsSignGids;
    }

    private string BbsForumId()
    {
        return _options.BbsForumId;
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

    private static string GetAward(IReadOnlyList<MihoyoLunaAward> awards, int index)
    {
        if (index < 0 || index >= awards.Count)
        {
            return "未知";
        }

        var award = awards[index];
        return $"「{award.Name}」x{award.Cnt}";
    }

    private static Task DelayAsync(int minMs, int maxMs, CancellationToken cancellationToken)
    {
        return Task.Delay(Random.Shared.Next(minMs, maxMs), cancellationToken);
    }
}

public sealed record MihoyoBbsSignResult(MihoyoAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record MihoyoGameSignResult(MihoyoAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record MihoyoAutoSignResult(MihoyoAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}
