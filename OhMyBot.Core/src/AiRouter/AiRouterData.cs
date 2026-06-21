using System.Text.Json.Serialization;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterUserData
{
    public string? DisplayName { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Username { get; set; }
}

public sealed class AiRouterSignInInfo
{
    public bool Enabled { get; set; }

    public bool CanSignIn { get; set; }

    public bool SignedInToday { get; set; }

    public string? BlockedCode { get; set; }

    public string? BlockedMessage { get; set; }

    public decimal TodayReward { get; set; }

    public long CurrentStreak { get; set; }

    public decimal TotalReward { get; set; }

    public long MonthSignedDays { get; set; }
}

public sealed class AiRouterLoginResponse
{
    public string? AccessToken { get; set; }

    public AiRouterUserData? User { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }
}

public sealed class AiRouterRewardCenterResponse
{
    public AiRouterSignInInfo? SignIn { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }
}

public sealed class AiRouterSignInResponse
{
    public AiRouterSignInInfo? SignIn { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }
}

public sealed record AiRouterApiResult<T>(int StatusCode, T? Data, string? RawBody)
{
    public bool IsSuccessStatusCode => StatusCode is >= 200 and < 300;

    public string ErrorMessage => Data switch
    {
        AiRouterLoginResponse login => login.Message ?? login.Code ?? RawBody ?? "未知错误",
        AiRouterRewardCenterResponse reward => reward.Message ?? reward.Code ?? RawBody ?? "未知错误",
        AiRouterSignInResponse sign => sign.Message ?? sign.Code ?? RawBody ?? "未知错误",
        _ => RawBody ?? "未知错误"
    };
}
