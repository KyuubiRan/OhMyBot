using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl.Http;
using Flurl.Http.Configuration;
using OhMyLib.Requests.AiRouter.Data;

namespace OhMyLib.Requests.AiRouter;

public sealed class AiRouterHttpClient : IDisposable
{
    private const string BaseUrl = "https://ai.router.team";
    private readonly FlurlClient _httpClient;

    private static readonly Dictionary<string, string> BasicHeaders = new()
    {
        { "Accept", "application/json" },
        { "Content-Type", "application/json" },
        { "User-Agent", RequestConstants.UserAgent },
    };

    public AiRouterHttpClient()
    {
        _httpClient = new FlurlClient(BaseUrl);
        _httpClient.WithSettings(x => x.JsonSerializer = new DefaultJsonSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        }));
    }

    private async Task<AiRouterApiResult<T>> ReceiveAsync<T>(IFlurlResponse response)
    {
        var raw = await response.GetStringAsync();
        T? data = default;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            data = JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            });
        }

        return new AiRouterApiResult<T>(response.StatusCode, data, raw);
    }

    public async Task<AiRouterApiResult<AiRouterLoginResponse>> LoginAsync(string username, string password)
    {
        var response = await _httpClient.Request("/api/auth/login")
                                        .WithHeaders(BasicHeaders)
                                        .AllowAnyHttpStatus()
                                        .PostJsonAsync(new { username, password });

        return await ReceiveAsync<AiRouterLoginResponse>(response);
    }

    public async Task<AiRouterApiResult<AiRouterRewardCenterResponse>> GetRewardCenterAsync(string accessToken)
    {
        var response = await _httpClient.Request("/api/user/reward-center")
                                        .WithHeaders(BasicHeaders)
                                        .WithOAuthBearerToken(accessToken)
                                        .AllowAnyHttpStatus()
                                        .GetAsync();

        return await ReceiveAsync<AiRouterRewardCenterResponse>(response);
    }

    public async Task<AiRouterApiResult<AiRouterSignInResponse>> SignInAsync(string accessToken)
    {
        var response = await _httpClient.Request("/api/user/reward-center/sign-in")
                                        .WithHeaders(BasicHeaders)
                                        .WithOAuthBearerToken(accessToken)
                                        .AllowAnyHttpStatus()
                                        .PostAsync();

        return await ReceiveAsync<AiRouterSignInResponse>(response);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
