using Microsoft.Extensions.Logging;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Skland;

public sealed class SklandSignService(
    SklandHttpClient client,
    SklandAccountService accountService,
    ILogger<SklandSignService> logger)
{
    // ---- Public API ----

    /// <summary>执行指定账号下所有已启用角色的游戏签到（自动签到路径）。</summary>
    public async Task<SklandAutoSignResult> ExecuteAutoSignAsync(
        SklandAccount account,
        CancellationToken cancellationToken = default)
    {
        var game = await ExecuteGameSignAsync(account, onlyEnabledAutoSign: true, cancellationToken: cancellationToken);
        return new SklandAutoSignResult(account, game.Lines);
    }

    /// <summary>
    /// 执行游戏签到。
    /// requestedRoleIds 为空时签所有角色（受 onlyEnabledAutoSign 过滤）。
    /// </summary>
    public async Task<SklandGameSignResult> ExecuteGameSignAsync(
        SklandAccount account,
        IEnumerable<long>? requestedRoleIds = null,
        bool onlyEnabledAutoSign = false,
        bool includeMissingConfigMessage = false,
        CancellationToken cancellationToken = default)
    {
        var (signToken, cred) = await accountService.GetValidTokensAsync(account, cancellationToken);
        var lines = new List<string>();
        var targets = ResolveRoleTargets(account, requestedRoleIds, onlyEnabledAutoSign, includeMissingConfigMessage, lines).ToArray();

        foreach (var role in targets)
        {
            // 角色标题行由外层统一写入；结果/失败行追加其后，重试时截断到此处即可。
            lines.Add($"[{role.GameName}] {role.NickName}（{role.Uid}）");
            var resultStart = lines.Count;
            try
            {
                (signToken, cred) = await ProcessRoleAsync(role, account, signToken, cred, lines, cancellationToken);
            }
            catch (SklandCredExpiredException)
            {
                throw;
            }
            catch (SklandUnauthorizedException)
            {
                // sign/cred 失效（HTTP 401）：刷新令牌后整角色重试一次。
                TruncateTo(lines, resultStart);
                try
                {
                    await accountService.RefreshSignTokenAsync(account, cancellationToken);
                    (signToken, cred) = await accountService.GetValidTokensAsync(account, cancellationToken);
                    (signToken, cred) = await ProcessRoleAsync(role, account, signToken, cred, lines, cancellationToken);
                }
                catch (SklandCredExpiredException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skland sign retry failed for account {AccountId} role {RoleId}.", account.Id, role.Id);
                    TruncateTo(lines, resultStart);
                    lines.Add("签到失败：" + ex.GetBaseException().Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skland sign failed for account {AccountId} role {RoleId}.", account.Id, role.Id);
                TruncateTo(lines, resultStart);
                lines.Add("签到失败：" + ex.GetBaseException().Message);
            }
        }

        return new SklandGameSignResult(account, lines);
    }

    private static void TruncateTo(List<string> lines, int count)
    {
        if (lines.Count > count)
        {
            lines.RemoveRange(count, lines.Count - count);
        }
    }

    // ---- Role processing ----

    /// <summary>返回处理后的 sign token（可能因 refresh 而更新）。</summary>
    private async Task<(string SignToken, string Cred)> ProcessRoleAsync(
        SklandGameRole role,
        SklandAccount account,
        string signToken,
        string cred,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (role.GameId == SklandGameNames.Arknights)
        {
            (signToken, cred) = await ProcessArknightsRoleAsync(role, account, signToken, cred, lines, cancellationToken);
        }
        else if (role.GameId == SklandGameNames.Endfield)
        {
            (signToken, cred) = await ProcessEndfieldRoleAsync(role, account, signToken, cred, lines, cancellationToken);
        }
        else
        {
            lines.Add("不支持的游戏 ID：" + role.GameId);
        }

        return (signToken, cred);
    }

    private async Task<(string SignToken, string Cred)> ProcessArknightsRoleAsync(
        SklandGameRole role,
        SklandAccount account,
        string signToken,
        string cred,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        // 查签到状态
        var statusResult = await client.GetArknightsAttendanceAsync(role.Uid, signToken, cred, account.DeviceId, cancellationToken);
        (signToken, cred) = await HandleTokenRefreshAsync(statusResult, account, signToken, cred, cancellationToken);
        if (!statusResult.IsOk)
        {
            // 重试一次（token 刷新后）
            statusResult = await client.GetArknightsAttendanceAsync(role.Uid, signToken, cred, account.DeviceId, cancellationToken);
        }

        ThrowIfApiFailed(statusResult, "获取签到状态失败");

        if (IsArknightsAlreadySigned(statusResult.Data))
        {
            var todayAward = GetTodayArknightsAward(statusResult.Data);
            lines.Add("签到结果：今日已签到");
            if (!string.IsNullOrEmpty(todayAward))
            {
                lines.Add("今日奖励：" + todayAward);
            }

            return (signToken, cred);
        }

        // 执行签到
        await DelayAsync(1000, 3000, cancellationToken);
        var signResult = await client.SignArknightsAsync(role.Uid, signToken, cred, account.DeviceId, cancellationToken);
        (signToken, cred) = await HandleTokenRefreshAsync(signResult, account, signToken, cred, cancellationToken);

        if (signResult.IsOk)
        {
            lines.Add("签到结果：成功");
            var awards = FormatArknightsAwards(signResult.Data?.Awards);
            if (!string.IsNullOrEmpty(awards))
            {
                lines.Add("今日奖励：" + awards);
            }

            logger.LogInformation(
                "Skland Arknights sign finished for account {AccountId}, uid={Uid}.",
                account.Id, role.Uid);
        }
        else
        {
            lines.Add("签到结果：失败 — " + signResult.Message);
            logger.LogWarning(
                "Skland Arknights sign failed for account {AccountId}, uid={Uid}, code={Code}, msg={Msg}.",
                account.Id, role.Uid, signResult.Code, signResult.Message);
        }

        return (signToken, cred);
    }

    private async Task<(string SignToken, string Cred)> ProcessEndfieldRoleAsync(
        SklandGameRole role,
        SklandAccount account,
        string signToken,
        string cred,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(role.RoleId) || string.IsNullOrEmpty(role.ServerId))
        {
            lines.Add("跳过：终末地角色缺少 roleId/serverId，请使用 /skland game init 同步角色");
            return (signToken, cred);
        }

        // 查签到状态
        var statusResult = await client.GetEndfieldAttendanceAsync(role.RoleId, role.ServerId, signToken, cred, account.DeviceId, cancellationToken);
        (signToken, cred) = await HandleTokenRefreshAsync(statusResult, account, signToken, cred, cancellationToken);
        if (!statusResult.IsOk)
        {
            statusResult = await client.GetEndfieldAttendanceAsync(role.RoleId, role.ServerId, signToken, cred, account.DeviceId, cancellationToken);
        }

        ThrowIfApiFailed(statusResult, "获取终末地签到状态失败");

        if (statusResult.Data?.HasToday == true)
        {
            lines.Add("签到结果：今日已签到");
            var award = FormatEndfieldAwards(statusResult.Data);
            if (!string.IsNullOrEmpty(award))
            {
                lines.Add("今日奖励：" + award);
            }

            return (signToken, cred);
        }

        await DelayAsync(1000, 3000, cancellationToken);
        var signResult = await client.SignEndfieldAsync(role.RoleId, role.ServerId, signToken, cred, account.DeviceId, cancellationToken);
        (signToken, cred) = await HandleTokenRefreshAsync(signResult, account, signToken, cred, cancellationToken);

        if (signResult.IsOk)
        {
            lines.Add("签到结果：成功");
            var award = FormatEndfieldSignAwards(signResult.Data);
            if (!string.IsNullOrEmpty(award))
            {
                lines.Add("今日奖励：" + award);
            }

            logger.LogInformation(
                "Skland Endfield sign finished for account {AccountId}, roleId={RoleId}.",
                account.Id, role.RoleId);
        }
        else
        {
            lines.Add("签到结果：失败 — " + signResult.Message);
            logger.LogWarning(
                "Skland Endfield sign failed for account {AccountId}, roleId={RoleId}, code={Code}, msg={Msg}.",
                account.Id, role.RoleId, signResult.Code, signResult.Message);
        }

        return (signToken, cred);
    }

    // ---- Token expiry handling ----

    /// <summary>
    /// 检查响应是否因 token 过期而失败；若是则刷新并返回新 token/cred。
    /// 调用方应在刷新后重试原始请求。
    /// </summary>
    private async Task<(string SignToken, string Cred)> HandleTokenRefreshAsync(
        SklandBaseResponse response,
        SklandAccount account,
        string signToken,
        string cred,
        CancellationToken cancellationToken)
    {
        if (response.IsOk || response.Code is not (SklandHttpClient.TokenExpiredCode or SklandHttpClient.CredExpiredCode))
        {
            return (signToken, cred);
        }

        if (response.Code == SklandHttpClient.CredExpiredCode)
        {
            await accountService.ClearCredAsync(account.Id, cancellationToken);
            throw new SklandCredExpiredException(account.Id);
        }

        // Sign token 过期 — 刷新
        var newToken = await accountService.RefreshSignTokenAsync(account, cancellationToken);
        return (newToken, cred);
    }

    // ---- Role target resolution ----

    private static IEnumerable<SklandGameRole> ResolveRoleTargets(
        SklandAccount account,
        IEnumerable<long>? requestedRoleIds,
        bool onlyEnabledAutoSign,
        bool includeMissingConfigMessage,
        ICollection<string> lines)
    {
        var requestedList = requestedRoleIds?.Distinct().ToArray();
        if (requestedList is { Length: > 0 })
        {
            foreach (var roleId in requestedList)
            {
                var role = account.Roles.FirstOrDefault(r => r.Id == roleId);
                if (role is null)
                {
                    if (includeMissingConfigMessage)
                    {
                        lines.Add($"未找到角色 #{roleId}，请使用 /skland game init {account.Id} 同步角色");
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

    // ---- Award formatting ----

    private static bool IsArknightsAlreadySigned(SklandAttendanceData? data)
    {
        if (data is null)
        {
            return false;
        }

        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;
        return data.Records.Any(r =>
            DateTimeOffset.FromUnixTimeSeconds(r.Timestamp).ToOffset(TimeSpan.FromHours(8)).Date == today);
    }

    private static string GetTodayArknightsAward(SklandAttendanceData? data)
    {
        if (data is null || data.Awards.Count == 0)
        {
            return string.Empty;
        }

        var record = data.Records
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefault();
        if (record is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(record.ResourceName) ? string.Empty : record.ResourceName;
    }

    private static string FormatArknightsAwards(IReadOnlyList<SklandAwardItem>? awards)
    {
        if (awards is null or { Count: 0 })
        {
            return string.Empty;
        }

        return string.Join("、", awards.Select(a => $"{a.Resource.Name} x{a.Count}"));
    }

    private static string FormatEndfieldAwards(SklandEndfieldAttendanceData? data)
    {
        if (data is null || data.ResourceInfoMap.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("、", data.ResourceInfoMap.Values.Select(r => $"{r.Name} x{r.Count}"));
    }

    private static string FormatEndfieldSignAwards(SklandEndfieldAttendanceSignResult? data)
    {
        if (data is null || data.ResourceInfoMap.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("、", data.ResourceInfoMap.Values.Select(r => $"{r.Name} x{r.Count}"));
    }

    private static void ThrowIfApiFailed(SklandBaseResponse response, string prefix)
    {
        if (response.IsOk)
        {
            return;
        }

        throw new InvalidOperationException($"{prefix}：code={response.Code}, msg={response.Message}");
    }

    private static Task DelayAsync(int minMs, int maxMs, CancellationToken cancellationToken)
    {
        return Task.Delay(Random.Shared.Next(minMs, maxMs), cancellationToken);
    }
}

public sealed record SklandGameSignResult(SklandAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}

public sealed record SklandAutoSignResult(SklandAccount Account, IReadOnlyList<string> Lines)
{
    public bool HasResult => Lines.Count > 0;
}
