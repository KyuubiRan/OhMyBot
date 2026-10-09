using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Plugin.Commanding.SignIn;

namespace OhMyBot.Tests;

[TestClass]
public sealed class SignInExecutorTests
{
    private static SignInExecutor CreateExecutor() => new(
        NullLogger<SignInExecutor>.Instance, retryDelay: TimeSpan.Zero);

    [TestMethod]
    public async Task TransientFailureIsRetriedThreeTimesAndSuccessfulModulesAreNotReplayed()
    {
        var executor = CreateExecutor();
        var communityCalls = 0;
        var gameCalls = 0;
        var lines = await executor.RunModulesAsync([
            new("社区签到", _ =>
            {
                communityCalls++;
                return Task.FromResult<IReadOnlyList<string>>(["社区签到成功"]);
            }),
            new("游戏签到", async token =>
            {
                return await executor.ExecuteWithRetryAsync<IReadOnlyList<string>>("游戏签到", _ =>
                {
                    gameCalls++;
                    if (gameCalls < 4) throw new HttpRequestException("Temporary service failure", null, HttpStatusCode.ServiceUnavailable);
                    return Task.FromResult<IReadOnlyList<string>>(["游戏签到成功"]);
                }, token);
            })
        ]);

        Assert.AreEqual(1, communityCalls);
        Assert.AreEqual(4, gameCalls);
        CollectionAssert.AreEqual(new[] { "社区签到成功", "游戏签到成功" }, lines.ToArray());
    }

    [TestMethod]
    public async Task ExhaustedCommunityTimeoutStillRunsGameAndExplainsRetries()
    {
        var executor = CreateExecutor();
        var attempts = 0;
        var lines = await executor.RunModulesAsync([
            new("社区签到", token => executor.ExecuteWithRetryAsync<IReadOnlyList<string>>("社区请求", _ =>
            {
                attempts++;
                throw new TaskCanceledException("The configured HttpClient.Timeout elapsed.",
                    new TimeoutException("Canceled", new IOException("Operation canceled")));
            }, token)),
            new("游戏签到", _ => Task.FromResult<IReadOnlyList<string>>(["游戏签到成功"]))
        ]);

        Assert.AreEqual(4, attempts);
        Assert.Contains("社区签到执行失败：接口请求超时", lines[0]);
        Assert.Contains("已重试 3 次", lines[0]);
        Assert.DoesNotContain("Operation canceled", lines[0]);
        Assert.AreEqual("游戏签到成功", lines[1]);
    }

    [TestMethod]
    public async Task PermanentCredentialFailureIsNotRetriedAndDoesNotBlockOtherModules()
    {
        var executor = CreateExecutor();
        var attempts = 0;
        var lines = await executor.RunModulesAsync([
            new("社区签到", token => executor.ExecuteWithRetryAsync<IReadOnlyList<string>>("社区请求", _ =>
            {
                attempts++;
                throw new CommandUserException("Expired", "社区凭据已失效，请重新绑定");
            }, token)),
            new("游戏签到", _ => Task.FromResult<IReadOnlyList<string>>(["游戏签到成功"]))
        ]);

        Assert.AreEqual(1, attempts);
        Assert.Contains("社区凭据已失效", lines[0]);
        Assert.AreEqual("游戏签到成功", lines[1]);
    }

    [TestMethod]
    public async Task CancellationStopsRetriesAndFollowingModules()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = CreateExecutor();
        var attempts = 0;
        var gameCalls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.RunModulesAsync([
            new("社区签到", token => executor.ExecuteWithRetryAsync<IReadOnlyList<string>>("社区请求", _ =>
            {
                attempts++;
                cancellation.Cancel();
                throw new HttpRequestException("Canceled request");
            }, token)),
            new("游戏签到", _ =>
            {
                gameCalls++;
                return Task.FromResult<IReadOnlyList<string>>(["游戏签到成功"]);
            })
        ], cancellation.Token));

        Assert.AreEqual(1, attempts);
        Assert.AreEqual(0, gameCalls);
    }
}
