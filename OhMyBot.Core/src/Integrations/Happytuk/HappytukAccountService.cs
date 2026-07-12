using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Security;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukAccountService(
    OhMyBotV2DbContext dbContext,
    HappytukHttpClient client,
    HappytukBrowserClient browserClient,
    ISecretProtector secretProtector,
    IDistributedCache cache,
    IOptions<HappytukOptions> options,
    TimeProvider timeProvider)
{
    private readonly HappytukOptions _options = options.Value;

    public Task<List<HappytukAccount>> ListByOwnerAsync(
        long coreUserId,
        bool noTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = noTracking ? dbContext.HappytukAccounts.AsNoTracking() : dbContext.HappytukAccounts;
        return query.Where(account => account.CoreUserId == coreUserId).OrderBy(account => account.Id).ToListAsync(cancellationToken);
    }

    public Task<List<HappytukAccount>> ListAutoRedeemTargetsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return dbContext.HappytukAccounts
            .AsNoTracking()
            .Include(account => account.CoreUser)
            .Where(account => account.CoreUser.Privilege > UserPrivilege.User)
            .OrderBy(account => account.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<HappytukAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return dbContext.HappytukAccounts.AsNoTracking().FirstOrDefaultAsync(account => account.Id == id, cancellationToken);
    }

    public async Task<HappytukAccount> BindAsync(
        long coreUserId,
        string loginAccount,
        string password,
        Func<CancellationToken, Task>? onBrowserFallback = null,
        CancellationToken cancellationToken = default)
    {
        loginAccount = loginAccount.Trim();
        if (string.IsNullOrWhiteSpace(loginAccount) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("账号和密码不能为空");
        }

        var session = await LoginAsync(loginAccount, password, onBrowserFallback, cancellationToken);
        var account = await dbContext.HappytukAccounts.FirstOrDefaultAsync(item => item.LoginAccount == loginAccount, cancellationToken);
        if (account is not null && account.CoreUserId != coreUserId)
        {
            throw new InvalidOperationException("该 HappyTuk 账号已被其他用户绑定");
        }

        var now = timeProvider.GetUtcNow();
        if (account is null)
        {
            account = new HappytukAccount
            {
                CoreUserId = coreUserId,
                LoginAccount = loginAccount,
                PasswordCiphertext = secretProtector.Protect(password),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.HappytukAccounts.Add(account);
        }
        else
        {
            account.PasswordCiphertext = secretProtector.Protect(password);
            account.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SetSessionAsync(account.Id, session, cancellationToken);
        return account;
    }

    public string DecryptPassword(HappytukAccount account) => secretProtector.Unprotect(account.PasswordCiphertext);

    public async Task<string> RefreshSessionAsync(HappytukAccount account, CancellationToken cancellationToken = default)
    {
        var session = await LoginAsync(account.LoginAccount, DecryptPassword(account), null, cancellationToken);
        await SetSessionAsync(account.Id, session, cancellationToken);
        return session;
    }

    public async Task<bool> DeleteAsync(long coreUserId, long accountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.HappytukAccounts
            .FirstOrDefaultAsync(item => item.Id == accountId && item.CoreUserId == coreUserId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        var subscriptions = await dbContext.NotificationSubscriptions
            .Where(item => item.CoreUserId == coreUserId
                && item.NotificationType == NotificationTypes.HappytukAutoRedeem
                && item.TargetId == accountId)
            .ToListAsync(cancellationToken);
        dbContext.NotificationSubscriptions.RemoveRange(subscriptions);
        dbContext.HappytukAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SessionKey(accountId), cancellationToken);
        return true;
    }

    public Task<string?> GetSessionAsync(long accountId, CancellationToken cancellationToken = default) =>
        cache.GetStringAsync(SessionKey(accountId), cancellationToken);

    public Task SetSessionAsync(long accountId, string session, CancellationToken cancellationToken = default) =>
        cache.SetStringAsync(SessionKey(accountId), session, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.SessionTtl
        }, cancellationToken);

    private static string SessionKey(long accountId) => $"happytuk:session:{accountId}";

    private async Task<string> LoginAsync(
        string loginAccount,
        string password,
        Func<CancellationToken, Task>? onBrowserFallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.LoginAsync(loginAccount, password, cancellationToken);
        }
        catch (HappytukBrowserRequiredException) when (_options.BrowserFallback.Enabled)
        {
            if (onBrowserFallback is not null)
            {
                await onBrowserFallback(cancellationToken);
            }

            return await browserClient.LoginAsync(loginAccount, password, cancellationToken);
        }
    }
}
