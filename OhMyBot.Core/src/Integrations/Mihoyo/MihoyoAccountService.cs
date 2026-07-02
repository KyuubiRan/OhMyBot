using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Security;

namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed partial class MihoyoAccountService(
    OhMyBotV2DbContext dbContext,
    MihoyoHttpClient client,
    ISecretProtector secretProtector,
    TimeProvider timeProvider)
{
    [GeneratedRegex(@"(?:account_id|ltuid|login_uid|ltuid_v2|account_id_v2)=(\d+)")]
    private static partial Regex StuidRegex();

    [GeneratedRegex(@"(?:account_mid_v2|ltmid_v2|mid)=([^;]+)")]
    private static partial Regex MidRegex();

    [GeneratedRegex(@"stoken=([^;]+)")]
    private static partial Regex StokenRegex();

    [GeneratedRegex(@"cookie_token=([^;]*)")]
    private static partial Regex CookieTokenRegex();

    public async Task<MihoyoBindResult> BindAsync(long coreUserId, string cookieInput, MihoyoRegion region, CancellationToken cancellationToken = default)
    {
        var cookie = NormalizeCookie(cookieInput);
        if (cookie.Length == 0)
        {
            throw new InvalidOperationException("Cookie 不能为空");
        }

        var stuidMatch = StuidRegex().Match(cookie);
        if (!stuidMatch.Success || !long.TryParse(stuidMatch.Groups[1].Value, out var stuid))
        {
            throw new InvalidOperationException("Cookie 缺少 account_id/ltuid，请重新抓取米游社/HoYoLAB 的 Cookie");
        }

        var mid = MidRegex().Match(cookie) is { Success: true } midMatch ? midMatch.Groups[1].Value : string.Empty;
        var stoken = StokenRegex().Match(cookie) is { Success: true } stokenMatch ? stokenMatch.Groups[1].Value : string.Empty;

        if (region == MihoyoRegion.Cn)
        {
            if (!string.IsNullOrEmpty(stoken))
            {
                if (stoken.StartsWith("v2_", StringComparison.Ordinal) && string.IsNullOrEmpty(mid))
                {
                    throw new InvalidOperationException("v2 版 stoken 需要 mid 参数，请抓取包含 mid 的 Cookie");
                }

                // 有 stoken：刷新 cookie_token 校验有效性并写入最新 token
                var stokenCookie = BuildStokenCookie(stuid, stoken, mid);
                var tokenResponse = await client.RefreshCookieTokenAsync(stokenCookie, cancellationToken);
                if (!tokenResponse.Ok || string.IsNullOrEmpty(tokenResponse.Data?.CookieToken))
                {
                    throw new InvalidOperationException($"stoken 校验失败（code={tokenResponse.Retcode}, msg={tokenResponse.Message}），请重新抓取 Cookie");
                }

                cookie = SetCookieToken(cookie, tokenResponse.Data.CookieToken);
            }
            else
            {
                // 无 stoken：仅能用 cookie_token 做游戏签到，且无法自动续期、无法做社区任务
                if (!CookieTokenRegex().IsMatch(cookie))
                {
                    throw new InvalidOperationException("国服 Cookie 缺少 cookie_token，请重新抓取登录后的 Cookie（建议包含 stoken 以支持自动续期和社区任务）");
                }

                var probe = await client.GetGameRolesAsync(cookie, "hk4e_cn", cancellationToken);
                if (probe.Retcode == MihoyoHttpClient.CookieExpiredCode)
                {
                    throw new InvalidOperationException("cookie_token 已失效，且 Cookie 中没有 stoken 无法自动刷新；请重新抓取 Cookie（建议包含 stoken）");
                }
            }
        }
        else if (!cookie.Contains("ltoken", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("国际服 Cookie 缺少 ltoken，请从 HoYoLAB 重新抓取 Cookie");
        }

        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.MihoyoAccounts
            .Include(account => account.Roles)
            .FirstOrDefaultAsync(account => account.Region == region && account.Stuid == stuid, cancellationToken);
        if (existing is not null && existing.CoreUserId != coreUserId)
        {
            throw new InvalidOperationException("该米游社账号已被其他用户绑定");
        }

        var updatedExisting = existing is not null;
        if (existing is null)
        {
            existing = new MihoyoAccount
            {
                CoreUserId = coreUserId,
                Region = region,
                Stuid = stuid,
                DisplayName = stuid.ToString(),
                CreatedAt = now
            };
            dbContext.MihoyoAccounts.Add(existing);
        }

        existing.CookieCiphertext = secretProtector.Protect(cookie);
        existing.StokenCiphertext = string.IsNullOrEmpty(stoken) ? string.Empty : secretProtector.Protect(stoken);
        existing.Mid = mid;
        existing.UpdatedAt = now;

        // 同步角色 + 显示名
        var credential = new MihoyoCredential(region, cookie, stoken, stuid, mid);
        await SyncRolesAsync(existing, credential, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return new MihoyoBindResult(existing, updatedExisting);
    }

    public Task<List<MihoyoAccount>> ListByOwnerAsync(long coreUserId, bool noTracking = false, CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.MihoyoAccounts.AsNoTracking() : dbContext.MihoyoAccounts;
        return query
            .Include(account => account.Roles)
            .Where(account => account.CoreUserId == coreUserId)
            .OrderBy(account => account.Region)
            .ThenBy(account => account.DisplayName)
            .ThenBy(account => account.Stuid)
            .ToListAsync(cancellationToken);
    }

    public Task<MihoyoAccount?> FindByIdAsync(long accountId, bool noTracking = false, CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.MihoyoAccounts.AsNoTracking() : dbContext.MihoyoAccounts;
        return query
            .Include(account => account.Roles)
            .FirstOrDefaultAsync(account => account.Id == accountId, cancellationToken);
    }

    public Task<List<MihoyoAccount>> ListAutoSignTargetsAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        return dbContext.MihoyoAccounts
            .AsNoTracking()
            .Include(account => account.CoreUser)
            .Where(account => account.AutoSignEnabled && account.CookieCiphertext != string.Empty && account.CoreUser.Privilege > UserPrivilege.User)
            .OrderBy(account => account.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<MihoyoAccount> RefreshRolesAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MihoyoAccounts
            .Include(item => item.Roles)
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken)
            ?? throw new InvalidOperationException("未找到指定米游社账号");
        var now = timeProvider.GetUtcNow();
        await SyncRolesAsync(account, GetCredential(account), now, cancellationToken);
        account.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<List<MihoyoAccount>> ToggleAutoSignAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return [];
        }

        account.AutoSignEnabled = !account.AutoSignEnabled;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    /// <summary>
    /// 原子翻转手动游戏签到的某个游戏勾选状态并持久化，返回最新勾选集合（按目录顺序）。
    /// 以账号为粒度串行化读改写，避免并发点击互相覆盖。
    /// </summary>
    public async Task<IReadOnlyList<string>> ToggleGameSignSelectionAsync(
        long coreUserId, long accountId, string gameKey, CancellationToken cancellationToken = default)
    {
        var gate = SelectionLocks[(int)((ulong)accountId % (ulong)SelectionLocks.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = await dbContext.MihoyoAccounts
                .Include(item => item.Roles)
                .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
            if (account is null)
            {
                return [];
            }

            var set = new HashSet<string>(MihoyoResponseBuilder.ResolveGameSignSelection(account), StringComparer.OrdinalIgnoreCase);
            if (!set.Remove(gameKey))
            {
                set.Add(gameKey);
            }

            var ordered = MihoyoResponseBuilder.AvailableGameKeys(account).Where(set.Contains).ToArray();
            account.GameSignSelection = MihoyoResponseBuilder.SerializeGameSignSelection(ordered);
            account.UpdatedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return ordered;
        }
        finally
        {
            gate.Release();
        }
    }

    private static readonly SemaphoreSlim[] SelectionLocks =
        Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<List<MihoyoAccount>> ToggleBbsTaskAsync(long coreUserId, long accountId, long taskFlag, CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return [];
        }

        account.BbsTaskFlags = (account.BbsTaskFlags & taskFlag) == 0
            ? account.BbsTaskFlags | taskFlag
            : account.BbsTaskFlags & ~taskFlag;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<MihoyoAccount>> ToggleAllBbsTasksAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return [];
        }

        account.BbsTaskFlags = account.BbsTaskFlags == MihoyoBbsTaskFlags.All
            ? MihoyoBbsTaskFlags.None
            : MihoyoBbsTaskFlags.All;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<MihoyoAccount>> ToggleGameAutoSignAsync(long coreUserId, long roleId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.MihoyoGameRoles
            .Include(item => item.MihoyoAccount)
            .FirstOrDefaultAsync(item => item.Id == roleId && item.MihoyoAccount.CoreUserId == coreUserId, cancellationToken);
        if (role is null)
        {
            return [];
        }

        role.AutoSignEnabled = !role.AutoSignEnabled;
        role.UpdatedAt = timeProvider.GetUtcNow();
        role.MihoyoAccount.UpdatedAt = role.UpdatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
    }

    public async Task<List<MihoyoAccount>> ToggleAllGameAutoSignAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MihoyoAccounts
            .Include(item => item.Roles)
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return [];
        }

        var enabled = account.Roles.Any(role => !role.AutoSignEnabled);
        var now = timeProvider.GetUtcNow();
        foreach (var role in account.Roles)
        {
            role.AutoSignEnabled = enabled;
            role.UpdatedAt = now;
        }

        account.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
    }

    public async Task<bool> DeleteAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MihoyoAccounts
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        dbContext.MihoyoAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>凭证失效：清空 Cookie/stoken 密文，保留 AutoSignEnabled（对齐 Kuro 策略）。</summary>
    public async Task ClearCredentialAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MihoyoAccounts.FirstOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.CookieCiphertext = string.Empty;
        account.StokenCiphertext = string.Empty;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>刷新后的 cookie_token 写回数据库。</summary>
    public async Task UpdateCookieAsync(long accountId, string cookie, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.MihoyoAccounts.FirstOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.CookieCiphertext = secretProtector.Protect(cookie);
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public MihoyoCredential GetCredential(MihoyoAccount account)
    {
        if (string.IsNullOrEmpty(account.CookieCiphertext))
        {
            throw new InvalidOperationException("Cookie 已失效，请重新绑定米游社账号");
        }

        var cookie = secretProtector.Unprotect(account.CookieCiphertext);
        var stoken = string.IsNullOrEmpty(account.StokenCiphertext) ? string.Empty : secretProtector.Unprotect(account.StokenCiphertext);
        return new MihoyoCredential(account.Region, cookie, stoken, account.Stuid, account.Mid);
    }

    public static string SetCookieToken(string cookie, string cookieToken)
    {
        if (CookieTokenRegex().IsMatch(cookie))
        {
            return CookieTokenRegex().Replace(cookie, $"cookie_token={cookieToken}");
        }

        return cookie.TrimEnd(';', ' ') + $";cookie_token={cookieToken}";
    }

    public static string BuildStokenCookie(long stuid, string stoken, string mid)
    {
        var cookie = $"stuid={stuid};stoken={stoken}";
        if (stoken.StartsWith("v2_", StringComparison.Ordinal) && !string.IsNullOrEmpty(mid))
        {
            cookie += $";mid={mid}";
        }

        return cookie;
    }

    private async Task SyncRolesAsync(MihoyoAccount account, MihoyoCredential credential, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (account.Region == MihoyoRegion.Os)
        {
            SeedOsRoles(account, now);
            return;
        }

        // 账号显示名取米游社昵称（best-effort，失败则保留 UID）
        try
        {
            var userInfo = await client.GetBbsUserInfoAsync(account.Stuid, cancellationToken);
            if (userInfo.Ok && !string.IsNullOrWhiteSpace(userInfo.Data?.UserInfo.Nickname))
            {
                account.DisplayName = userInfo.Data.UserInfo.Nickname;
            }
        }
        catch (Exception)
        {
            // 保留 UID 作为显示名
        }

        foreach (var game in MihoyoGameCatalog.ForRegion(MihoyoRegion.Cn))
        {
            MihoyoApiResponse<MihoyoGameRolesData> response;
            try
            {
                response = await client.GetGameRolesAsync(credential.Cookie, game.CnGameBiz, cancellationToken);
            }
            catch (Exception)
            {
                continue;
            }

            if (response.Retcode == MihoyoHttpClient.CookieExpiredCode)
            {
                continue;
            }

            if (!response.Ok || response.Data is null)
            {
                continue;
            }

            foreach (var item in response.Data.List)
            {
                if (!long.TryParse(item.GameUid, out var gameUid))
                {
                    continue;
                }

                UpsertRole(account, game.CnGameBiz, game.Name, item.Region, gameUid, item.Nickname, item.Level.ToString(), now);
            }
        }
    }

    private static void SeedOsRoles(MihoyoAccount account, DateTimeOffset now)
    {
        foreach (var game in MihoyoGameCatalog.ForRegion(MihoyoRegion.Os))
        {
            UpsertRole(account, game.CnGameBiz, game.Name, region: "os", gameUid: 0, nickname: string.Empty, level: string.Empty, now);
        }
    }

    private static void UpsertRole(
        MihoyoAccount account,
        string gameBiz,
        string gameName,
        string region,
        long gameUid,
        string nickname,
        string level,
        DateTimeOffset now)
    {
        var existing = account.Roles.FirstOrDefault(role => role.GameBiz == gameBiz && role.GameUid == gameUid);
        if (existing is null)
        {
            account.Roles.Add(new MihoyoGameRole
            {
                GameBiz = gameBiz,
                GameName = gameName,
                Region = region,
                GameUid = gameUid,
                Nickname = nickname,
                Level = level,
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        existing.GameName = gameName;
        existing.Region = string.IsNullOrEmpty(region) ? existing.Region : region;
        existing.Nickname = string.IsNullOrEmpty(nickname) ? existing.Nickname : nickname;
        existing.Level = string.IsNullOrEmpty(level) ? existing.Level : level;
        existing.UpdatedAt = now;
    }

    private static string NormalizeCookie(string cookie)
    {
        return cookie.Trim().Trim('"');
    }
}

public sealed record MihoyoBindResult(MihoyoAccount Account, bool UpdatedExisting);

public sealed record MihoyoCredential(MihoyoRegion Region, string Cookie, string Stoken, long Stuid, string Mid)
{
    public string StokenCookie => MihoyoAccountService.BuildStokenCookie(Stuid, Stoken, Mid);

    public string DeviceId => MihoyoDs.GetDeviceId(Stuid.ToString());
}
