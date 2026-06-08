using FoxTail.Extensions;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Models.AiRouter;
using OhMyLib.Requests.AiRouter;
using OhMyLib.Requests.AiRouter.Data;

namespace OhMyLib.Services.AiRouter;

[Component]
public class AiRouterSignService(AiRouterAccountService accountService, ILogger<AiRouterSignService> logger)
{
    public async Task<AiRouterSignResult> SignInAsync(AiRouterAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var client = new AiRouterHttpClient();
        var token = await accountService.GetTokenAsync(account.Id, cancellationToken);
        var refreshed = false;

        if (token.IsWhiteSpaceOrNull)
        {
            token = await LoginAndCacheTokenAsync(client, account, cancellationToken);
            refreshed = true;
        }

        var reward = await client.GetRewardCenterAsync(token!);
        if (!IsValidRewardResponse(reward))
        {
            token = await LoginAndCacheTokenAsync(client, account, cancellationToken);
            refreshed = true;
            reward = await client.GetRewardCenterAsync(token);
        }

        if (!reward.IsSuccessStatusCode)
            return AiRouterSignResult.Failed(account.Account, $"获取签到状态失败 [{reward.StatusCode}]：{reward.ErrorMessage}");

        var signInfo = reward.Data?.SignIn;
        if (signInfo == null)
            return AiRouterSignResult.Failed(account.Account, "签到数据为空");

        if (signInfo.CanSignIn)
        {
            var sign = await client.SignInAsync(token!);
            if (!sign.IsSuccessStatusCode)
            {
                logger.LogWarning("AI Router sign in failed for account {Account}, status={StatusCode}, message={Message}",
                                  account.Account,
                                  sign.StatusCode,
                                  sign.ErrorMessage);
                return AiRouterSignResult.Failed(account.Account, $"签到失败 [{sign.StatusCode}]：{sign.ErrorMessage}");
            }

            var signedInfo = sign.Data?.SignIn ?? signInfo;
            var message = sign.Data?.Message;
            if (message.IsWhiteSpaceOrNull)
                message = "签到成功";
            return AiRouterSignResult.Success(account.Account, message, signedInfo, refreshed);
        }

        if (signInfo.SignedInToday)
            return AiRouterSignResult.AlreadySigned(account.Account, signInfo, refreshed);

        var reason = signInfo.BlockedMessage;
        if (reason.IsWhiteSpaceOrNull)
            reason = "当前不可签到（可能需要绑定手机号/微信）";

        return AiRouterSignResult.Failed(account.Account, reason, signInfo);
    }

    private async Task<string> LoginAndCacheTokenAsync(AiRouterHttpClient client, AiRouterAccount account, CancellationToken cancellationToken)
    {
        var login = await client.LoginAsync(account.Account, account.Password);
        if (!login.IsSuccessStatusCode || string.IsNullOrWhiteSpace(login.Data?.AccessToken))
            throw new InvalidOperationException($"AI Router 登录失败 [{login.StatusCode}]：{login.ErrorMessage}");

        await accountService.SetTokenAsync(account.Id, login.Data.AccessToken!, cancellationToken);
        return login.Data.AccessToken!;
    }

    private static bool IsValidRewardResponse(AiRouterApiResult<AiRouterRewardCenterResponse> response) =>
        response.IsSuccessStatusCode && response.Data?.SignIn != null;
}

public enum AiRouterSignResultType
{
    Success,
    AlreadySigned,
    Failed,
}

public sealed record AiRouterSignResult(
    string Account,
    AiRouterSignResultType Type,
    string Message,
    AiRouterSignInInfo? SignIn,
    bool TokenRefreshed)
{
    public static AiRouterSignResult Success(string account, string message, AiRouterSignInInfo signIn, bool tokenRefreshed) =>
        new(account, AiRouterSignResultType.Success, message, signIn, tokenRefreshed);

    public static AiRouterSignResult AlreadySigned(string account, AiRouterSignInInfo signIn, bool tokenRefreshed) =>
        new(account, AiRouterSignResultType.AlreadySigned, "今日已签到，无需重复签到", signIn, tokenRefreshed);

    public static AiRouterSignResult Failed(string account, string message, AiRouterSignInInfo? signIn = null) =>
        new(account, AiRouterSignResultType.Failed, message, signIn, false);

    public string ToMessage()
    {
        var status = Type switch
        {
            AiRouterSignResultType.Success => "签到成功",
            AiRouterSignResultType.AlreadySigned => "今日已签到",
            _ => "签到失败"
        };

        var lines = new List<string>
        {
            $"账号：`{Account}`",
            $"结果：{status}",
            $"说明：{Message}"
        };

        if (SignIn != null)
        {
            lines.Add($"今日奖励：{SignIn.TodayReward:F2}");
            lines.Add($"连续签到：{SignIn.CurrentStreak} 天");
            lines.Add($"累计奖励：{SignIn.TotalReward:F2}");
            lines.Add($"本月签到：{SignIn.MonthSignedDays} 天");
        }

        return string.Join('\n', lines);
    }
}
