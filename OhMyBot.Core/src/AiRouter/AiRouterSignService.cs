using Microsoft.Extensions.Logging;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterSignService(
    AiRouterAccountService accountService,
    AiRouterHttpClient client,
    ILogger<AiRouterSignService> logger)
{
    public async Task<AiRouterSignResult> SignInAsync(
        AiRouterAccount account,
        CancellationToken cancellationToken = default)
    {
        var token = await accountService.GetTokenAsync(account.Id, cancellationToken);
        var refreshed = false;

        if (string.IsNullOrWhiteSpace(token))
        {
            token = await LoginAndCacheTokenAsync(account, cancellationToken);
            refreshed = true;
        }

        var reward = await client.GetRewardCenterAsync(token!, cancellationToken);
        if (!IsValidRewardResponse(reward))
        {
            token = await LoginAndCacheTokenAsync(account, cancellationToken);
            refreshed = true;
            reward = await client.GetRewardCenterAsync(token, cancellationToken);
        }

        if (!reward.IsSuccessStatusCode)
        {
            return AiRouterSignResult.Failed(account.Id, account.LoginEmail, account.DisplayName, $"获取签到状态失败 [{reward.StatusCode}]：{reward.ErrorMessage}");
        }

        var signInfo = reward.Data?.SignIn;
        if (signInfo is null)
        {
            return AiRouterSignResult.Failed(account.Id, account.LoginEmail, account.DisplayName, "签到数据为空");
        }

        if (signInfo.CanSignIn)
        {
            var sign = await client.SignInAsync(token!, cancellationToken);
            if (!sign.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "AI Router sign in failed for account {AccountId}, status={StatusCode}, message={Message}",
                    account.Id,
                    sign.StatusCode,
                    sign.ErrorMessage);
                return AiRouterSignResult.Failed(account.Id, account.LoginEmail, account.DisplayName, $"签到失败 [{sign.StatusCode}]：{sign.ErrorMessage}", signInfo);
            }

            return AiRouterSignResult.Success(
                account.Id,
                account.LoginEmail,
                account.DisplayName,
                string.IsNullOrWhiteSpace(sign.Data?.Message) ? "签到成功" : sign.Data.Message!,
                sign.Data?.SignIn ?? signInfo,
                refreshed);
        }

        if (signInfo.SignedInToday)
        {
            return AiRouterSignResult.AlreadySigned(account.Id, account.LoginEmail, account.DisplayName, signInfo, refreshed);
        }

        return AiRouterSignResult.Failed(
            account.Id,
            account.LoginEmail,
            account.DisplayName,
            string.IsNullOrWhiteSpace(signInfo.BlockedMessage) ? "当前不可签到（可能需要绑定手机号/微信）" : signInfo.BlockedMessage!,
            signInfo);
    }

    private async Task<string> LoginAndCacheTokenAsync(AiRouterAccount account, CancellationToken cancellationToken)
    {
        var password = accountService.DecryptPassword(account);
        var login = await client.LoginAsync(account.LoginEmail, password, cancellationToken);
        if (!login.IsSuccessStatusCode || string.IsNullOrWhiteSpace(login.Data?.AccessToken))
        {
            throw new InvalidOperationException($"AI Router 登录失败 [{login.StatusCode}]：{login.ErrorMessage}");
        }

        await accountService.SetTokenAsync(account.Id, login.Data.AccessToken!, cancellationToken);
        return login.Data.AccessToken!;
    }

    private static bool IsValidRewardResponse(AiRouterApiResult<AiRouterRewardCenterResponse> response)
    {
        return response.IsSuccessStatusCode && response.Data?.SignIn is not null;
    }
}

public enum AiRouterSignResultType
{
    Success,
    AlreadySigned,
    Failed
}

public sealed record AiRouterSignResult(
    long AccountId,
    string LoginEmail,
    string DisplayName,
    AiRouterSignResultType Type,
    string Message,
    AiRouterSignInInfo? SignIn,
    bool TokenRefreshed)
{
    public static AiRouterSignResult Success(
        long accountId,
        string loginEmail,
        string displayName,
        string message,
        AiRouterSignInInfo signIn,
        bool tokenRefreshed) =>
        new(accountId, loginEmail, displayName, AiRouterSignResultType.Success, message, signIn, tokenRefreshed);

    public static AiRouterSignResult AlreadySigned(
        long accountId,
        string loginEmail,
        string displayName,
        AiRouterSignInInfo signIn,
        bool tokenRefreshed) =>
        new(accountId, loginEmail, displayName, AiRouterSignResultType.AlreadySigned, "今日已签到，无需重复签到", signIn, tokenRefreshed);

    public static AiRouterSignResult Failed(
        long accountId,
        string loginEmail,
        string displayName,
        string message,
        AiRouterSignInInfo? signIn = null) =>
        new(accountId, loginEmail, displayName, AiRouterSignResultType.Failed, message, signIn, false);
}
