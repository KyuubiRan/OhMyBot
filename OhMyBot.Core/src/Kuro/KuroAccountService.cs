using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Security;

namespace OhMyBot.Core.Kuro;

public sealed class KuroAccountService(
    OhMyBotV2DbContext dbContext,
    KuroHttpClient client,
    ISecretProtector secretProtector,
    TimeProvider timeProvider)
{
    public async Task<KuroBindResult> BindAsync(
        long coreUserId,
        string token,
        string? devCode = null,
        string? distinctId = null,
        CancellationToken cancellationToken = default)
    {
        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token 不能为空");
        }

        var credential = new KuroRequestCredential(token, devCode, distinctId);
        var profile = await ResolveProfileAsync(credential, cancellationToken);
        var existing = await dbContext.KuroAccounts
            .Include(account => account.Roles)
            .FirstOrDefaultAsync(account => account.BbsUserId == profile.BbsUserId, cancellationToken);
        if (existing is not null && existing.CoreUserId != coreUserId)
        {
            throw new InvalidOperationException("该库街区账号已被其他用户绑定");
        }

        var now = timeProvider.GetUtcNow();
        var updatedExisting = existing is not null;
        if (existing is null)
        {
            existing = new KuroAccount
            {
                CoreUserId = coreUserId,
                BbsUserId = profile.BbsUserId,
                DisplayName = profile.DisplayName,
                TokenCiphertext = secretProtector.Protect(token),
                DevCode = devCode?.Trim() ?? string.Empty,
                DistinctId = distinctId?.Trim() ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.KuroAccounts.Add(existing);
        }
        else
        {
            existing.DisplayName = profile.DisplayName;
            existing.TokenCiphertext = secretProtector.Protect(token);
            existing.DevCode = devCode?.Trim() ?? existing.DevCode;
            existing.DistinctId = distinctId?.Trim() ?? existing.DistinctId;
            existing.UpdatedAt = now;
        }

        SyncRoles(existing, profile.Roles, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new KuroBindResult(existing, updatedExisting);
    }

    public Task<List<KuroAccount>> ListByOwnerAsync(
        long coreUserId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.KuroAccounts.AsNoTracking() : dbContext.KuroAccounts;
        return query
            .Include(account => account.Roles)
            .Where(account => account.CoreUserId == coreUserId)
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.BbsUserId)
            .ToListAsync(cancellationToken);
    }

    public Task<KuroAccount?> FindByIdAsync(
        long accountId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.KuroAccounts.AsNoTracking() : dbContext.KuroAccounts;
        return query
            .Include(account => account.Roles)
            .FirstOrDefaultAsync(account => account.Id == accountId, cancellationToken);
    }

    public Task<List<KuroAccount>> ListAutoSignTargetsAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        return dbContext.KuroAccounts
            .AsNoTracking()
            .Include(account => account.CoreUser)
            .Where(account => account.AutoSignEnabled && account.TokenCiphertext != string.Empty && account.CoreUser.Privilege > UserPrivilege.User)
            .OrderBy(account => account.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<KuroAccount> RefreshRolesAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.KuroAccounts
            .Include(item => item.Roles)
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken)
            ?? throw new InvalidOperationException("未找到指定库街区账号");
        var profile = await ResolveProfileAsync(GetCredential(account), cancellationToken);
        if (profile.BbsUserId != account.BbsUserId)
        {
            throw new InvalidOperationException("Token 对应的库街区账号与当前绑定不一致，请重新绑定");
        }

        account.DisplayName = profile.DisplayName;
        account.UpdatedAt = timeProvider.GetUtcNow();
        SyncRoles(account, profile.Roles, account.UpdatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<List<KuroAccount>> ToggleAutoSignAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
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

    public async Task<List<KuroAccount>> ToggleBbsTaskAsync(
        long coreUserId,
        long accountId,
        long taskFlag,
        CancellationToken cancellationToken = default)
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

    public async Task<List<KuroAccount>> ToggleAllBbsTasksAsync(
        long coreUserId,
        long accountId,
        CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return [];
        }

        account.BbsTaskFlags = account.BbsTaskFlags == KuroBbsTaskFlags.All
            ? KuroBbsTaskFlags.None
            : KuroBbsTaskFlags.All;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<KuroAccount>> ToggleGameAutoSignAsync(
        long coreUserId,
        long roleId,
        CancellationToken cancellationToken = default)
    {
        var role = await dbContext.KuroGameRoles
            .Include(item => item.KuroAccount)
            .FirstOrDefaultAsync(item => item.Id == roleId && item.KuroAccount.CoreUserId == coreUserId, cancellationToken);
        if (role is null)
        {
            return [];
        }

        role.AutoSignEnabled = !role.AutoSignEnabled;
        role.UpdatedAt = timeProvider.GetUtcNow();
        role.KuroAccount.UpdatedAt = role.UpdatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
    }

    public async Task<List<KuroAccount>> ToggleAllGameAutoSignAsync(
        long coreUserId,
        long accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await dbContext.KuroAccounts
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
        var account = await dbContext.KuroAccounts
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        dbContext.KuroAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ClearTokenAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.KuroAccounts.FirstOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        account.TokenCiphertext = string.Empty;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public string DecryptToken(KuroAccount account)
    {
        if (string.IsNullOrEmpty(account.TokenCiphertext))
        {
            throw new InvalidOperationException("Token 已失效，请重新绑定库街区账号");
        }

        return secretProtector.Unprotect(account.TokenCiphertext);
    }

    public KuroRequestCredential GetCredential(KuroAccount account)
    {
        return new KuroRequestCredential(DecryptToken(account), account.DevCode, account.DistinctId);
    }

    private async Task<KuroResolvedProfile> ResolveProfileAsync(KuroRequestCredential credential, CancellationToken cancellationToken)
    {
        var mine = await client.GetMineAsync(credential, cancellationToken);
        ThrowIfApiFailed(mine, "获取库街区用户信息失败");
        var userIdText = mine.Data?.Mine?.UserId;
        if (!long.TryParse(userIdText, out var bbsUserId))
        {
            throw new InvalidOperationException("获取库街区用户信息失败：返回用户 ID 为空");
        }

        var displayName = string.IsNullOrWhiteSpace(mine.Data?.Mine?.UserName)
            ? bbsUserId.ToString()
            : mine.Data.Mine.UserName!;
        var defaults = await client.GetDefaultRolesAsync(credential, bbsUserId, cancellationToken);
        ThrowIfApiFailed(defaults, "获取库街区默认角色失败");
        var roles = defaults.Data?.DefaultRoleList
            .Where(role => long.TryParse(role.RoleId, out _))
            .Select(role => new KuroResolvedRole(
                role.GameId,
                KuroGameNames.Format(role.GameId, role.ServerName),
                role.ServerId,
                role.ServerName,
                long.Parse(role.RoleId),
                role.RoleName,
                role.GameLevel))
            .ToArray() ?? [];
        return new KuroResolvedProfile(bbsUserId, displayName, roles);
    }

    private static void ThrowIfApiFailed(KuroBaseResponse response, string prefix)
    {
        if (response.Success)
        {
            return;
        }

        if (response.Code == KuroHttpClient.TokenExpiredCode)
        {
            throw new InvalidOperationException($"{prefix}：库街区返回 Token 失效。{FormatApiResponse(response)}");
        }

        throw new InvalidOperationException($"{prefix}。{FormatApiResponse(response)}");
    }

    private static string FormatApiResponse(KuroBaseResponse response)
    {
        var raw = string.IsNullOrWhiteSpace(response.Raw)
            ? string.Empty
            : " raw=" + Truncate(response.Raw, 500);
        return $"code={response.Code}, msg={response.Msg}, success={response.Success}{raw}";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static void SyncRoles(KuroAccount account, IReadOnlyList<KuroResolvedRole> roles, DateTimeOffset now)
    {
        foreach (var role in roles)
        {
            var existing = account.Roles.FirstOrDefault(item =>
                item.GameId == role.GameId
                && item.ServerId == role.ServerId
                && item.RoleId == role.RoleId);
            if (existing is null)
            {
                account.Roles.Add(new KuroGameRole
                {
                    GameId = role.GameId,
                    GameName = role.GameName,
                    ServerId = role.ServerId,
                    ServerName = role.ServerName,
                    RoleId = role.RoleId,
                    RoleName = role.RoleName,
                    GameLevel = role.GameLevel,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            existing.GameName = role.GameName;
            existing.ServerName = role.ServerName;
            existing.RoleName = role.RoleName;
            existing.GameLevel = role.GameLevel;
            existing.UpdatedAt = now;
        }
    }
}

public sealed record KuroBindResult(KuroAccount Account, bool UpdatedExisting);

public sealed record KuroResolvedProfile(long BbsUserId, string DisplayName, IReadOnlyList<KuroResolvedRole> Roles);

public sealed record KuroResolvedRole(
    long GameId,
    string GameName,
    string ServerId,
    string ServerName,
    long RoleId,
    string RoleName,
    string GameLevel);
