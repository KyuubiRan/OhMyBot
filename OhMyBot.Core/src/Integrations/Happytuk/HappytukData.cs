using System.Text.Json;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukLoginResponse
{
    public HappytukLoginResult? Result { get; set; }
}

public sealed class HappytukLoginResult
{
    public bool Ok { get; set; }

    public bool OtpUser { get; set; }

    public bool RequireCaptcha { get; set; }

    public string? Msg { get; set; }
}

public sealed class HappytukCouponResponse
{
    public JsonElement ReturnCode { get; set; }

    public string? Checksum { get; set; }

    public HappytukRequiredSelection? ServerData { get; set; }

    public HappytukRequiredSelection? CharacterData { get; set; }
}

public sealed class HappytukRequiredSelection
{
    public bool Required { get; set; }
}

public enum HappytukRedeemResultType
{
    Success,
    AlreadyRedeemed,
    SessionExpired,
    Failed
}

public sealed record HappytukRedeemResult(
    long AccountId,
    string LoginAccount,
    string CouponCode,
    HappytukRedeemResultType Type,
    string Message);

public sealed class HappytukBrowserRequiredException(string message) : InvalidOperationException(message);

// A cached session must carry the User-Agent it was minted with: cf_clearance is only accepted
// on requests that repeat the same IP + UA, so the redeem calls have to reuse this exact UA.
public sealed record HappytukSession(string Cookies, string UserAgent)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static HappytukSession Parse(string raw, string fallbackUserAgent)
    {
        if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<HappytukSession>(raw, Options);
                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Cookies))
                {
                    return string.IsNullOrWhiteSpace(parsed.UserAgent)
                        ? parsed with { UserAgent = fallbackUserAgent }
                        : parsed;
                }
            }
            catch (JsonException)
            {
                // Fall through to treating the raw value as a bare cookie header.
            }
        }

        return new HappytukSession(raw, fallbackUserAgent);
    }
}
