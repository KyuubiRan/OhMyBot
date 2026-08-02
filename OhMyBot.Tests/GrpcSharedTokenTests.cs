using OhMyBot.Contracts;

namespace OhMyBot.Tests;

/// <summary>
/// 共享令牌文件是 Core → 网关的自动分发通道，替代「同一个令牌手抄三份」。
/// 它没有放宽鉴权（令牌仍然必填、仍然逐请求校验，见 <see cref="GrpcAccessTokenTests"/>），
/// 因此这里验证的是分发本身的三条边界：显式配置能覆盖、重启不换令牌、别的 uid 读不到。
/// </summary>
[TestClass]
public class GrpcSharedTokenTests
{
    private string _runtimeDirectory = string.Empty;
    private string? _originalXdgRuntimeDir;

    [TestInitialize]
    public void SetUp()
    {
        // ResolvePath 每次都现读环境变量，把它指到临时目录即可隔离真实的 $XDG_RUNTIME_DIR。
        _originalXdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        _runtimeDirectory = Path.Combine(Path.GetTempPath(), $"ohmybot-token-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_runtimeDirectory);
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _runtimeDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _originalXdgRuntimeDir);
        if (Directory.Exists(_runtimeDirectory))
        {
            Directory.Delete(_runtimeDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ExplicitConfigurationWinsOverSharedFile()
    {
        // 跨机部署时网关和 Core 不在同一台机器，本机文件里的令牌必然是别的值（或是自己那台 Core 的）。
        // 显式配置一旦被文件盖掉，跨机部署就会静默连错令牌，且现象是难查的 Unauthenticated。
        using var lease = GrpcSharedToken.Acquire();

        var resolved = GrpcSharedToken.Resolve("explicitly-configured-token");

        Assert.AreEqual("explicitly-configured-token", resolved);
        Assert.AreNotEqual(lease.Token, resolved);
    }

    [TestMethod]
    public void MissingConfigurationFallsBackToSharedFile()
    {
        using var lease = GrpcSharedToken.Acquire();

        // 空白串等同没配：模板里 AccessToken 就是 ""，不能被当成一个真的令牌拿去鉴权。
        Assert.AreEqual(lease.Token, GrpcSharedToken.Resolve(null));
        Assert.AreEqual(lease.Token, GrpcSharedToken.Resolve("   "));
    }

    [TestMethod]
    public void RestartReusesExistingTokenInsteadOfRotating()
    {
        // Core 重启若换新令牌，正在跑的两个网关会立刻集体 Unauthenticated，
        // 且它们不会自己重读文件——等于每次重启 Core 都要连带重启网关。
        string first;
        using (var lease = GrpcSharedToken.Acquire())
        {
            first = lease.Token;
        }

        using var afterRestart = GrpcSharedToken.Acquire();

        Assert.AreEqual(first, afterRestart.Token);
    }

    [TestMethod]
    public void SecondInstanceCannotAcquireWhileFirstHoldsLock()
    {
        // 双开 Core 时若两边都能写，网关拿到的令牌取决于谁最后落盘——
        // 表现为随机的鉴权失败。宁可第二个实例直接起不来。
        using var lease = GrpcSharedToken.Acquire();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => GrpcSharedToken.Acquire());

        // 撞上这个错误最常见的场景是「Core 已在 systemd 里跑着，我只是想开个控制台」，
        // 报错必须把人导向 --remote-console，否则会被误读成远程控制台坏了。
        StringAssert.Contains(exception.Message, "--remote-console");
        StringAssert.Contains(exception.Message, "Grpc:AccessToken");
    }

    [TestMethod]
    public void GatewayCanStillReadWhileCoreHoldsLock()
    {
        // 锁之所以放在单独的 .lock 文件上：.NET 在 Unix 上把 FileShare 映射成 flock，
        // FileShare.None 会连读者一起挡掉。若把锁直接加在令牌文件上，
        // 「防双开」就会以「网关永远读不到令牌」为代价——两个网关全部起不来。
        using var lease = GrpcSharedToken.Acquire();

        Assert.AreEqual(lease.Token, GrpcSharedToken.TryRead());
    }

    [TestMethod]
    public void TokenFileIsReadableOnlyByOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix 权限位不适用于 Windows。");
            return;
        }

        // 「同机才拿得到令牌」这条边界完全靠文件权限表达——不是靠判断请求来源是不是 127.0.0.1
        // （那样一挂反向代理，外部请求的来源也会变成 127.0.0.1）。
        // 权限一旦松成 0644，同机任意用户都能读到这个可执行提权命令的令牌。
        using var lease = GrpcSharedToken.Acquire();

        var mode = File.GetUnixFileMode(GrpcSharedToken.ResolvePath());

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [TestMethod]
    public void ResolveReturnsNullWhenNeitherConfiguredNorGenerated()
    {
        // 网关据此报出「先启动 Core / 跨机请显式配置」，而不是带着空令牌去连然后收一个裸的 Unauthenticated。
        Assert.IsNull(GrpcSharedToken.Resolve(null));
    }
}
