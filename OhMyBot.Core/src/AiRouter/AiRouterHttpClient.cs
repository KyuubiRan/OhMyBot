using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<AiRouterApiResult<AiRouterLoginResponse>> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent(new { username, password })
        };
        return await SendAsync<AiRouterLoginResponse>(request, cancellationToken);
    }

    public async Task<AiRouterApiResult<AiRouterRewardCenterResponse>> GetRewardCenterAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/reward-center");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync<AiRouterRewardCenterResponse>(request, cancellationToken);
    }

    public async Task<AiRouterApiResult<AiRouterSignInResponse>> SignInAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/reward-center/sign-in");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync<AiRouterSignInResponse>(request, cancellationToken);
    }

    private async Task<AiRouterApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!request.Headers.UserAgent.Any())
        {
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 OhMyBot/2.0");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = string.IsNullOrWhiteSpace(raw) ? default : JsonSerializer.Deserialize<T>(raw, JsonOptions);
        return new AiRouterApiResult<T>((int)response.StatusCode, data, raw);
    }

    private static StringContent JsonContent<T>(T payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
    }
}
