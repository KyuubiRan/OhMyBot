using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Security;

namespace OhMyBot.Core.Integrations.Skland;

public sealed class SklandAccountService(
    OhMyBotV2DbContext dbContext,
    SklandHttpClient client,
    ISecretProtector secretProtector,
    TimeProvider timeProvider)
{
    // ---- Bind / upsert ----

    public async Task<SklandBindResult> BindAsync(
        long coreUserId,
        string hgToken,
        CancellationToken cancellationToken = default)
    {
        hgToken = hgToken.Trim();
        if (string.IsNullOrWhiteSpace(hgToken))
        {
            throw new InvalidOperationException("鹰角 Token 不能为空");
        }

        var deviceId = NewDeviceId();
        var (cred, signToken, sklandUserId) = await AuthenticateAsync(hgToken, deviceId, cancellationToken);

        // 获取绑定角色列表
        var binding = await client.GetBindingAsync(signToken, cred, deviceId, cancellationToken);
        ThrowIfApiFailed(binding, "获取角色列表失败");

        // 获取森空岛平台昵称（/web/v1/user/me）；若 API 不可用则回落到游戏角色名
        var displayName = await ResolveDisplayNameAsync(signToken, cred, deviceId, binding.Data, cancellationToken);

        // 解析角色列表，并为明日方舟角色补充等级（/api/v1/game/player/info）
        var roles = await ResolveRolesAsync(signToken, cred, deviceId, binding.Data, cancellationToken);

        var existing = await dbContext.SklandAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.SklandUserId == sklandUserId, cancellationToken);
        if (existing is not null && existing.CoreUserId != coreUserId)
        {
            throw new InvalidOperationException("该森空岛账号已被其他用户绑定");
        }

        var now = timeProvider.GetUtcNow();
        var updatedExisting = existing is not null;
        if (existing is null)
        {
            existing = new SklandAccount
            {
                CoreUserId = coreUserId,
                SklandUserId = sklandUserId,
                DeviceId = deviceId,
                DisplayName = displayName,
                HgTokenCiphertext = secretProtector.Protect(hgToken),
                CredCiphertext = secretProtector.Protect(cred),
                SignTokenCiphertext = secretProtector.Protect(signToken),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.SklandAccounts.Add(existing);
        }
        else
        {
            existing.DisplayName = displayName;
            existing.HgTokenCiphertext = secretProtector.Protect(hgToken);
            existing.CredCiphertext = secretProtector.Protect(cred);
            existing.SignTokenCiphertext = secretProtector.Protect(signToken);
            existing.UpdatedAt = now;
        }

        SyncRoles(existing, roles, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SklandBindResult(existing, updatedExisting);
    }

    // ---- Queries ----

    public Task<List<SklandAccount>> ListByOwnerAsync(
        long coreUserId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var q = noTracking ? dbContext.SklandAccounts.AsNoTracking() : dbContext.SklandAccounts;
        return q
            .Include(a => a.Roles)
            .Where(a => a.CoreUserId == coreUserId)
            .OrderBy(a => a.DisplayName)
            .ThenBy(a => a.SklandUserId)
            .ToListAsync(cancellationToken);
    }

    public Task<SklandAccount?> FindByIdAsync(
        long accountId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var q = noTracking ? dbContext.SklandAccounts.AsNoTracking() : dbContext.SklandAccounts;
        return q
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
    }

    public Task<List<SklandAccount>> ListAutoSignTargetsAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        return dbContext.SklandAccounts
            .AsNoTracking()
            .Include(a => a.CoreUser)
            .Include(a => a.Roles)
            .Where(a => a.AutoSignEnabled && a.CredCiphertext != string.Empty && a.CoreUser.Privilege > UserPrivilege.User)
            .OrderBy(a => a.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    // ---- Role refresh ----

    public async Task<SklandAccount> RefreshRolesAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.SklandAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == accountId && a.CoreUserId == coreUserId, cancellationToken)
            ?? throw new InvalidOperationException("未找到指定森空岛账号");

        var (signToken, cred) = GetTokens(account);
        var binding = await client.GetBindingAsync(signToken, cred, account.DeviceId, cancellationToken);
        ThrowIfApiFailed(binding, "获取角色列表失败");

        var displayName = await ResolveDisplayNameAsync(signToken, cred, account.DeviceId, binding.Data, cancellationToken);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            account.DisplayName = displayName;
        }

        account.UpdatedAt = timeProvider.GetUtcNow();
        var roles = await ResolveRolesAsync(signToken, cred, account.DeviceId, binding.Data, cancellationToken);
        SyncRoles(account, roles, account.UpdatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    // ---- Toggle auto-sign ----

    public async Task<List<SklandAccount>> ToggleAutoSignAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
        {
            return [];
        }

        account.AutoSignEnabled = !account.AutoSignEnabled;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<SklandAccount>> ToggleGameAutoSignAsync(long coreUserId, long roleId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.SklandGameRoles
            .Include(r => r.SklandAccount)
            .FirstOrDefaultAsync(r => r.Id == roleId && r.SklandAccount.CoreUserId == coreUserId, cancellationToken);
        if (role is null)
        {
            return [];
        }

        role.AutoSignEnabled = !role.AutoSignEnabled;
        role.UpdatedAt = timeProvider.GetUtcNow();
        role.SklandAccount.UpdatedAt = role.UpdatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 原子翻转手动游戏签到的某个游戏类型勾选并持久化，返回最新勾选集合（按目录顺序）。
    /// 以账号为粒度串行化读改写，避免并发点击互相覆盖。
    /// </summary>
    public async Task<IReadOnlyList<string>> ToggleGameSignSelectionAsync(
        long coreUserId, long accountId, string gameKey, CancellationToken cancellationToken = default)
    {
        var gate = SelectionLocks[(int)((ulong)accountId % (ulong)SelectionLocks.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = await dbContext.SklandAccounts
                .Include(item => item.Roles)
                .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
            if (account is null)
            {
                return [];
            }

            var set = new HashSet<string>(SklandResponseBuilder.ResolveGameSignSelection(account), StringComparer.OrdinalIgnoreCase);
            if (!set.Remove(gameKey))
            {
                set.Add(gameKey);
            }

            var ordered = SklandResponseBuilder.AvailableGameKeys(account).Where(set.Contains).ToArray();
            account.GameSignSelection = SklandResponseBuilder.SerializeGameSignSelection(ordered);
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

    public async Task<List<SklandAccount>> ToggleAllGameAutoSignAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.SklandAccounts
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Id == accountId && a.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return [];
        }

        var enabled = account.Roles.Any(r => !r.AutoSignEnabled);
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

    // ---- Delete ----

    public async Task<bool> DeleteAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.SklandAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        dbContext.SklandAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- Credential management ----

    public async Task ClearCredAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.SklandAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.CredCiphertext = string.Empty;
        account.SignTokenCiphertext = string.Empty;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 获取有效的 sign token 和 cred。若 sign token 已被清空（过期），
    /// 先尝试 /web/v1/auth/refresh；若 cred 也失效则用 HG token 重新换取。
    /// </summary>
    public async Task<(string SignToken, string Cred)> GetValidTokensAsync(
        SklandAccount account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(account.CredCiphertext))
        {
            throw new SklandCredExpiredException(account.Id);
        }

        var cred = secretProtector.Unprotect(account.CredCiphertext);
        var signToken = string.IsNullOrEmpty(account.SignTokenCiphertext)
            ? string.Empty
            : secretProtector.Unprotect(account.SignTokenCiphertext);

        if (string.IsNullOrEmpty(signToken))
        {
            // sign token 已清空，刷新之
            signToken = await RefreshSignTokenInternalAsync(account, cred, cancellationToken);
        }

        return (signToken, cred);
    }

    /// <summary>刷新 sign token 并持久化，返回新 sign token。</summary>
    public async Task<string> RefreshSignTokenAsync(SklandAccount account, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(account.CredCiphertext))
        {
            throw new SklandCredExpiredException(account.Id);
        }

        var cred = secretProtector.Unprotect(account.CredCiphertext);
        return await RefreshSignTokenInternalAsync(account, cred, cancellationToken);
    }

    private async Task<string> RefreshSignTokenInternalAsync(
        SklandAccount account,
        string cred,
        CancellationToken cancellationToken)
    {
        // 需要一个当前 sign token 才能调用 refresh；若完全没有，重新走 HG 认证
        var currentSignToken = string.IsNullOrEmpty(account.SignTokenCiphertext)
            ? string.Empty
            : secretProtector.Unprotect(account.SignTokenCiphertext);

        if (!string.IsNullOrEmpty(currentSignToken))
        {
            try
            {
                var refreshResult = await client.RefreshTokenAsync(currentSignToken, cred, account.DeviceId, cancellationToken);
                if (refreshResult.IsOk && !string.IsNullOrEmpty(refreshResult.Data?.Token))
                {
                    await PersistSignTokenAsync(account, refreshResult.Data.Token, cancellationToken);
                    return refreshResult.Data.Token;
                }
            }
            catch (SklandUnauthorizedException)
            {
                // refresh 端点也拒绝了过期 sign token（401），回落到 HG 重新认证。
            }
        }

        // Refresh 失败，走 HG 重新认证
        if (string.IsNullOrEmpty(account.HgTokenCiphertext))
        {
            throw new SklandCredExpiredException(account.Id);
        }

        var hgToken = secretProtector.Unprotect(account.HgTokenCiphertext);
        var (newCred, newSignToken, _) = await AuthenticateAsync(hgToken, account.DeviceId, cancellationToken);
        await PersistCredAndSignTokenAsync(account, newCred, newSignToken, cancellationToken);
        return newSignToken;
    }

    private async Task PersistSignTokenAsync(SklandAccount account, string signToken, CancellationToken cancellationToken)
    {
        var tracked = await dbContext.SklandAccounts.FindAsync([account.Id], cancellationToken);
        if (tracked is null)
        {
            return;
        }

        tracked.SignTokenCiphertext = secretProtector.Protect(signToken);
        tracked.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        // 同步内存中的实体，避免调用方后续读到旧值
        account.SignTokenCiphertext = tracked.SignTokenCiphertext;
    }

    private async Task PersistCredAndSignTokenAsync(SklandAccount account, string cred, string signToken, CancellationToken cancellationToken)
    {
        var tracked = await dbContext.SklandAccounts.FindAsync([account.Id], cancellationToken);
        if (tracked is null)
        {
            return;
        }

        tracked.CredCiphertext = secretProtector.Protect(cred);
        tracked.SignTokenCiphertext = secretProtector.Protect(signToken);
        tracked.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        account.CredCiphertext = tracked.CredCiphertext;
        account.SignTokenCiphertext = tracked.SignTokenCiphertext;
    }

    // ---- Internal helpers ----

    /// <summary>从 HG token 走完两步认证，返回 (cred, signToken, sklandUserId)。</summary>
    private async Task<(string Cred, string SignToken, string SklandUserId)> AuthenticateAsync(
        string hgToken,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var grant = await client.GrantAsync(hgToken, cancellationToken);
        if (!grant.IsOk || string.IsNullOrEmpty(grant.Data?.Code))
        {
            throw new InvalidOperationException($"获取鹰角授权码失败：code={grant.Code}, msg={grant.Message}");
        }

        var cred = await client.GenerateCredByCodeAsync(grant.Data.Code, deviceId, cancellationToken);
        if (!cred.IsOk || cred.Data is null)
        {
            throw new InvalidOperationException($"获取森空岛凭证失败：code={cred.Code}, msg={cred.Message}");
        }

        return (cred.Data.Cred, cred.Data.Token, cred.Data.UserId);
    }

    private (string SignToken, string Cred) GetTokens(SklandAccount account)
    {
        if (string.IsNullOrEmpty(account.CredCiphertext))
        {
            throw new SklandCredExpiredException(account.Id);
        }

        var cred = secretProtector.Unprotect(account.CredCiphertext);
        var signToken = string.IsNullOrEmpty(account.SignTokenCiphertext)
            ? string.Empty
            : secretProtector.Unprotect(account.SignTokenCiphertext);
        return (signToken, cred);
    }

    private static string FallbackDisplayName(SklandBindingData? data)
    {
        if (data is null) return string.Empty;
        foreach (var app in data.List)
            foreach (var item in app.BindingList)
                if (!string.IsNullOrWhiteSpace(item.NickName))
                    return item.NickName;
        return string.Empty;
    }

    private async Task<string> ResolveDisplayNameAsync(
        string signToken, string cred, string deviceId,
        SklandBindingData? bindingData, CancellationToken cancellationToken)
    {
        try
        {
            var info = await client.GetUserInfoAsync(signToken, cred, deviceId, cancellationToken);
            if (info.IsOk && !string.IsNullOrWhiteSpace(info.Data?.User?.Nickname))
            {
                return info.Data.User.Nickname;
            }
        }
        catch
        {
            // 用户信息接口不可用时降级
        }

        return FallbackDisplayName(bindingData);
    }

    private async Task<IReadOnlyList<SklandResolvedRole>> ResolveRolesAsync(
        string signToken, string cred, string deviceId,
        SklandBindingData? data, CancellationToken cancellationToken)
    {
        if (data is null) return [];

        var roles = new List<SklandResolvedRole>();
        foreach (var app in data.List)
        {
            var gameId = SklandGameNames.FromAppCode(app.AppCode);
            if (gameId == 0) continue;

            foreach (var item in app.BindingList)
            {
                if (gameId == SklandGameNames.Arknights)
                {
                    // 绑定接口不返回等级，从 player/info 补充
                    var level = await FetchArknightsLevelAsync(item.Uid, signToken, cred, deviceId, cancellationToken);
                    roles.Add(new SklandResolvedRole(
                        gameId, app.AppCode, item.GameName,
                        item.Uid, item.NickName,
                        level, item.ChannelName,
                        ServerId: string.Empty, RoleId: string.Empty));
                }
                else if (gameId == SklandGameNames.Endfield)
                {
                    foreach (var roleEntry in item.Roles)
                    {
                        // 终末地的角色名和等级在 roles[] 里
                        var nick = string.IsNullOrWhiteSpace(roleEntry.Nickname) ? item.NickName : roleEntry.Nickname;
                        roles.Add(new SklandResolvedRole(
                            gameId, app.AppCode, item.GameName,
                            item.Uid, nick,
                            roleEntry.Level.ToString(), item.ChannelName,
                            roleEntry.ServerId, roleEntry.RoleId));
                    }

                    if (item.Roles.Count == 0)
                    {
                        roles.Add(new SklandResolvedRole(
                            gameId, app.AppCode, item.GameName,
                            item.Uid, item.NickName, string.Empty,
                            item.ChannelName, ServerId: string.Empty, RoleId: string.Empty));
                    }
                }
            }
        }

        return roles;
    }

    private async Task<string> FetchArknightsLevelAsync(
        string uid, string signToken, string cred, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var info = await client.GetPlayerInfoAsync(uid, SklandGameNames.Arknights, signToken, cred, deviceId, cancellationToken);
            if (info.IsOk && info.Data?.Status is { } s && s.Level > 0)
            {
                return s.Level.ToString();
            }
        }
        catch
        {
            // 接口不可用时返回空
        }

        return string.Empty;
    }

    private static void SyncRoles(SklandAccount account, IReadOnlyList<SklandResolvedRole> roles, DateTimeOffset now)
    {
        foreach (var role in roles)
        {
            var existing = account.Roles.FirstOrDefault(r =>
                r.GameId == role.GameId
                && r.Uid == role.Uid
                && r.RoleId == role.RoleId);
            if (existing is null)
            {
                account.Roles.Add(new SklandGameRole
                {
                    GameId = role.GameId,
                    AppCode = role.AppCode,
                    GameName = role.GameName,
                    Uid = role.Uid,
                    NickName = role.NickName,
                    Level = role.Level,
                    ChannelName = role.ChannelName,
                    ServerId = role.ServerId,
                    RoleId = role.RoleId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            existing.NickName = role.NickName;
            existing.Level = role.Level;
            existing.ChannelName = role.ChannelName;
            existing.ServerId = role.ServerId;
            existing.UpdatedAt = now;
        }
    }

    private static void ThrowIfApiFailed(SklandBaseResponse response, string prefix)
    {
        if (response.IsOk)
        {
            return;
        }

        throw new InvalidOperationException($"{prefix}：code={response.Code}, msg={response.Message}");
    }

    private static string NewDeviceId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

public sealed record SklandBindResult(SklandAccount Account, bool UpdatedExisting);

public sealed record SklandResolvedRole(
    int GameId,
    string AppCode,
    string GameName,
    string Uid,
    string NickName,
    string Level,
    string ChannelName,
    string ServerId,
    string RoleId);

public sealed class SklandCredExpiredException(long accountId)
    : Exception("森空岛凭证已失效，请重新绑定账号（使用 /skland bind <鹰角Token>）")
{
    public long AccountId { get; } = accountId;
}
