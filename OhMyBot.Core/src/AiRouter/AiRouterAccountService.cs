using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Security;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterAccountService(
    OhMyBotV2DbContext dbContext,
    AiRouterHttpClient client,
    ISecretProtector secretProtector,
    IDistributedCache cache,
    IOptions<AiRouterOptions> options,
    TimeProvider timeProvider)
{
    private readonly AiRouterOptions _options = options.Value;

    public Task<List<AiRouterAccount>> ListByOwnerAsync(
        long coreUserId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.AiRouterAccounts.AsNoTracking() : dbContext.AiRouterAccounts;
        return query
            .Where(account => account.CoreUserId == coreUserId)
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.LoginEmail)
            .ToListAsync(cancellationToken);
    }

    public Task<AiRouterAccount?> FindByIdAsync(
        long id,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.AiRouterAccounts.AsNoTracking() : dbContext.AiRouterAccounts;
        return query.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);
    }

    public Task<List<AiRouterAccount>> ListAutoSignTargetsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return dbContext.AiRouterAccounts
            .AsNoTracking()
            .Include(account => account.CoreUser)
            .Where(account => account.AutoSignEnabled && account.CoreUser.Privilege > UserPrivilege.User)
            .OrderBy(account => account.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiRouterBindResult> BindAsync(
        long coreUserId,
        string inputAccount,
        string password,
        CancellationToken cancellationToken = default)
    {
        inputAccount = inputAccount.Trim();
        if (string.IsNullOrWhiteSpace(inputAccount))
        {
            throw new InvalidOperationException("账号不能为空");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("密码不能为空");
        }

        var login = await client.LoginAsync(inputAccount, password, cancellationToken);
        if (!login.IsSuccessStatusCode || string.IsNullOrWhiteSpace(login.Data?.AccessToken))
        {
            throw new InvalidOperationException($"登录失败 [{login.StatusCode}]：{login.ErrorMessage}");
        }

        var loginEmail = FirstNonEmpty(login.Data.User?.Email, inputAccount);
        var displayName = FirstNonEmpty(
            login.Data.User?.DisplayName,
            login.Data.User?.Name,
            login.Data.User?.Email,
            login.Data.User?.Username,
            inputAccount);

        var existing = await dbContext.AiRouterAccounts
            .FirstOrDefaultAsync(account => account.LoginEmail == loginEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (existing is not null && existing.CoreUserId != coreUserId)
        {
            throw new InvalidOperationException("该 AI Router 账号已被其他用户绑定");
        }

        if (existing is null)
        {
            existing = new AiRouterAccount
            {
                CoreUserId = coreUserId,
                LoginEmail = loginEmail,
                DisplayName = displayName,
                PasswordCiphertext = secretProtector.Protect(password),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.AiRouterAccounts.Add(existing);
        }
        else
        {
            existing.DisplayName = displayName;
            existing.PasswordCiphertext = secretProtector.Protect(password);
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SetTokenAsync(existing.Id, login.Data.AccessToken!, cancellationToken);
        return new AiRouterBindResult(existing.Id, existing.LoginEmail, existing.DisplayName);
    }

    public async Task<bool> DeleteAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.AiRouterAccounts
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        dbContext.AiRouterAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(TokenCacheKey(account.Id), cancellationToken);
        return true;
    }

    public async Task<List<AiRouterAccount>> ToggleAutoSignAsync(
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

        account.AutoSignEnabled = !account.AutoSignEnabled;
        account.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<AiRouterAccount>> ToggleAllAutoSignAsync(long coreUserId, CancellationToken cancellationToken = default)
    {
        var accounts = await ListByOwnerAsync(coreUserId, cancellationToken: cancellationToken);
        if (accounts.Count == 0)
        {
            return accounts;
        }

        var enabled = accounts.Any(account => !account.AutoSignEnabled);
        var now = timeProvider.GetUtcNow();
        foreach (var account in accounts)
        {
            account.AutoSignEnabled = enabled;
            account.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public Task<string?> GetTokenAsync(long accountId, CancellationToken cancellationToken = default)
    {
        return cache.GetStringAsync(TokenCacheKey(accountId), cancellationToken);
    }

    public Task SetTokenAsync(long accountId, string accessToken, CancellationToken cancellationToken = default)
    {
        return cache.SetStringAsync(TokenCacheKey(accountId), accessToken, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.TokenTtl
        }, cancellationToken);
    }

    public string DecryptPassword(AiRouterAccount account)
    {
        return secretProtector.Unprotect(account.PasswordCiphertext);
    }

    private static string TokenCacheKey(long accountId) => $"ai-router:token:{accountId}";

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}

public sealed record AiRouterBindResult(long Id, string LoginEmail, string DisplayName);
