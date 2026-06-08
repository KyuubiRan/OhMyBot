using FoxTail.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Extensions;
using OhMyLib.Models.AiRouter;
using OhMyLib.Repositories;
using OhMyLib.Requests.AiRouter;
using OhMyLib.Requests.AiRouter.Data;

namespace OhMyLib.Services.AiRouter;

[Component]
public class AiRouterAccountService(
    AiRouterAccountRepo repo,
    BotUserService botUserService,
    IDistributedCache cache)
{
    private static string TokenCacheKey(long accountId) => $"ai_router:token:{accountId}";

    public async Task<IReadOnlyList<AiRouterAccount>> ListAccountsAsync(
        string ownerId,
        SoftwareType softwareType,
        bool noTracking = false,
        CancellationToken cancellationToken = default) =>
        await repo.ListByOwnerAsync(ownerId, softwareType, noTracking, cancellationToken);

    public async Task<AiRouterAccount?> FindByIdAsync(long id, bool noTracking = false, CancellationToken cancellationToken = default) =>
        await repo.FindByIdAsync(id, noTracking, cancellationToken);

    public async Task<AiRouterAccount?> FindByAccountAsync(string account, bool noTracking = false, CancellationToken cancellationToken = default) =>
        await repo.FindByAccountAsync(account, noTracking, cancellationToken);

    public async Task<AiRouterBindResult> BindAsync(
        string ownerId,
        SoftwareType softwareType,
        string account,
        string password,
        CancellationToken cancellationToken = default)
    {
        account = account.Trim();
        if (account.IsWhiteSpaceOrNull)
            throw new InvalidOperationException("账号不能为空");
        if (password.IsWhiteSpaceOrNull)
            throw new InvalidOperationException("密码不能为空");

        var owner = await botUserService.GetUserAsync(ownerId, softwareType, cancellationToken)
                    ?? throw new InvalidOperationException("Owner bot user not found.");

        using var client = new AiRouterHttpClient();
        var login = await client.LoginAsync(account, password);
        if (!login.IsSuccessStatusCode || string.IsNullOrWhiteSpace(login.Data?.AccessToken))
            throw new InvalidOperationException($"登录失败 [{login.StatusCode}]：{login.ErrorMessage}");

        var exists = await repo.FindByAccountAsync(account, cancellationToken: cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (exists != null && exists.OwnerUserId != owner.Id)
            throw new InvalidOperationException("该 AI Router 账号已被其他用户绑定");

        var entity = exists;
        if (entity == null)
        {
            entity = new AiRouterAccount
            {
                OwnerBotUser = owner,
                OwnerUserId = owner.Id,
                Account = account,
                Password = password,
                AutoSignEnabled = false,
                CreateAt = now,
                UpdateAt = now
            };
            await repo.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.Password = password;
            entity.UpdateAt = now;
        }

        await repo.SaveChangesAsync(cancellationToken);
        await SetTokenAsync(entity.Id, login.Data.AccessToken!, cancellationToken);

        var user = login.Data.User;
        var displayName = user?.Name;
        if (displayName.IsWhiteSpaceOrNull)
            displayName = user?.Email;
        if (displayName.IsWhiteSpaceOrNull)
            displayName = user?.Username;
        if (displayName.IsWhiteSpaceOrNull)
            displayName = account;
        return new AiRouterBindResult(entity.Id, entity.Account, displayName);
    }

    public async Task<bool> DeleteAsync(string ownerId, SoftwareType softwareType, string account, CancellationToken cancellationToken = default)
    {
        var entity = await repo.FindByAccountAsync(account, cancellationToken: cancellationToken);
        if (entity == null || entity.OwnerBotUser.OwnerId != ownerId || entity.OwnerBotUser.OwnerType != softwareType)
            return false;

        repo.Remove(entity);
        await repo.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(TokenCacheKey(entity.Id), cancellationToken);
        return true;
    }

    public async Task<AiRouterAccount?> SetAutoSignAsync(
        string ownerId,
        SoftwareType softwareType,
        string account,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var entity = await repo.FindByAccountAsync(account, cancellationToken: cancellationToken);
        if (entity == null || entity.OwnerBotUser.OwnerId != ownerId || entity.OwnerBotUser.OwnerType != softwareType)
            return null;

        entity.AutoSignEnabled = enabled;
        entity.UpdateAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<List<AiRouterAccount>> ToggleAutoSignAsync(
        string ownerId,
        SoftwareType softwareType,
        long accountId,
        CancellationToken cancellationToken = default)
    {
        var accounts = await repo.ListByOwnerAsync(ownerId, softwareType, cancellationToken: cancellationToken);
        var account = accounts.FirstOrDefault(x => x.Id == accountId);
        if (account == null)
            return [];

        account.AutoSignEnabled = !account.AutoSignEnabled;
        account.UpdateAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<AiRouterAccount>> ToggleAllAutoSignAsync(
        string ownerId,
        SoftwareType softwareType,
        CancellationToken cancellationToken = default)
    {
        var accounts = await repo.ListByOwnerAsync(ownerId, softwareType, cancellationToken: cancellationToken);
        if (accounts.Count == 0)
            return accounts;

        var enabled = accounts.Any(x => !x.AutoSignEnabled);
        foreach (var account in accounts)
        {
            account.AutoSignEnabled = enabled;
            account.UpdateAt = DateTimeOffset.UtcNow;
        }

        await repo.SaveChangesAsync(cancellationToken);
        return accounts;
    }

    public async Task<List<AiRouterAccount>> ListAutoSignTargetsAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        await repo.ListEnabledAutoSignAsync(offset, limit, cancellationToken);

    public async Task<string?> GetTokenAsync(long accountId, CancellationToken cancellationToken = default) =>
        await cache.GetObjectAsync<string>(TokenCacheKey(accountId), cancellationToken: cancellationToken);

    public async Task SetTokenAsync(long accountId, string accessToken, CancellationToken cancellationToken = default)
    {
        await cache.SetObjectAsync(TokenCacheKey(accountId), accessToken, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
        }, cancellationToken);
    }
}

public sealed record AiRouterBindResult(long Id, string Account, string DisplayName);
