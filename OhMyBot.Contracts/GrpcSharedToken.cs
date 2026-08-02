using System.Security.Cryptography;
using System.Text;

namespace OhMyBot.Contracts;

/// <summary>
/// 同机部署下 Core → 网关的令牌分发通道。Core 启动时把随机令牌写进运行时目录并持有一个锁文件，
/// 网关与远程控制台启动时读出来直接用，省掉「同一个令牌手抄三份」。
///
/// 这不是「localhost 免鉴权」的降级：令牌照常必填、照常被 AccessTokenInterceptor 逐请求校验，
/// 变的只是它从哪来。显式配置永远优先；跨机部署读不到这个文件，仍然必须自己配。
///
/// 「同机」这个边界由文件系统权限表达（目录 0700、文件 0600，只有同一个 uid 能读），
/// 不经过网络层，因此不存在反向代理把来源伪装成 127.0.0.1 导致鉴权失效的问题。
/// </summary>
public static class GrpcSharedToken
{
    private const string DirectoryName = "ohmybot";
    private const string FileName = "grpc-token";
    private const string LockSuffix = ".lock";

    /// <summary>
    /// 令牌文件路径。优先 $XDG_RUNTIME_DIR：systemd --user 下必定存在，本身就是 0700 且登出即清空，
    /// 正是为这类运行时状态准备的。取不到才退回临时目录（macOS 的 $TMPDIR 是每用户私有的；
    /// Linux 上裸 /tmp 则靠下面显式创建的 0700 目录兜底）。
    /// </summary>
    public static string ResolvePath()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var root = string.IsNullOrWhiteSpace(runtimeDirectory) ? Path.GetTempPath() : runtimeDirectory;
        return Path.Combine(root, DirectoryName, FileName);
    }

    /// <summary>
    /// 令牌解析的统一入口：显式配置优先，没配才回落到 Core 写下的共享文件。
    /// 两者都没有时返回 null，由调用方决定报什么错。
    /// </summary>
    public static string? Resolve(string? configured)
    {
        return string.IsNullOrWhiteSpace(configured) ? TryRead() : configured;
    }

    /// <summary>
    /// 读取 Core 写下的令牌。文件不存在、读不到、内容为空都返回 null——
    /// 这些都是「回落失败」而非异常，调用方会给出比 IO 异常更有用的提示。
    /// </summary>
    public static string? TryRead()
    {
        try
        {
            var path = ResolvePath();
            if (!File.Exists(path))
            {
                return null;
            }

            // 令牌文件本身不上锁（见 Acquire 里的说明），这里正常读即可。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var token = reader.ReadToEnd().Trim();
            return token.Length == 0 ? null : token;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Core 专用：取得令牌，并持有锁文件直到进程退出，挡住第二个 Core 实例。
    /// 已有令牌就复用——Core 重启不该让正在跑的网关集体失效。
    /// </summary>
    /// <exception cref="InvalidOperationException">锁已被另一个 Core 实例持有，或运行时目录不可写。</exception>
    public static GrpcSharedTokenLease Acquire()
    {
        var path = ResolvePath();
        var lockPath = path + LockSuffix;

        FileStream lockStream;
        try
        {
            CreateOwnerOnlyDirectory(Path.GetDirectoryName(path)!);

            // 锁必须落在单独的文件上：.NET 在 Unix 上把 FileShare 映射成 flock，
            // FileShare.None → LOCK_EX（连读者一起挡掉），其余一律 LOCK_SH（谁都挡不住）——
            // 没有「独占写但允许读」这档语义（那是 Win32 的模型）。
            // 所以锁 .lock、令牌写在 grpc-token，网关才能一边读一边被挡在写外面。
            lockStream = new FileStream(lockPath, CreateOwnerOnlyOptions(FileMode.OpenOrCreate, FileShare.None));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"无法获取共享令牌锁 {lockPath}：{exception.Message}。" +
                "通常是已经有一个 Core 实例在运行（systemd 服务，或另一个终端）。" +
                "若只是想连上它的管理控制台，请改用 --remote-console，不要再起一个实例；" +
                "确需另起实例请先停掉在跑的那个，或在 appsettings.json 里显式配置 Grpc:AccessToken 绕开自动分发。",
                exception);
        }

        try
        {
            var existing = TryRead();
            if (existing is not null)
            {
                return new GrpcSharedTokenLease(existing, lockStream);
            }

            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            // 先删再新建：UnixCreateMode 只作用于新建的文件，残留的空文件可能是 0644。
            File.Delete(path);
            using (var stream = new FileStream(path, CreateOwnerOnlyOptions(FileMode.CreateNew, FileShare.Read)))
            {
                stream.Write(Encoding.UTF8.GetBytes(generated));
                stream.Flush(flushToDisk: true);
            }

            return new GrpcSharedTokenLease(generated, lockStream);
        }
        catch
        {
            lockStream.Dispose();
            throw;
        }
    }

    /// <summary>文件 0600：同机边界最终靠这个兜底，别的 uid 读不到令牌。</summary>
    private static FileStreamOptions CreateOwnerOnlyOptions(FileMode mode, FileShare share)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.ReadWrite,
            Share = share
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static void CreateOwnerOnlyDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

/// <summary>
/// 持有共享令牌锁文件的租约。释放（或进程退出由 OS 回收 flock）后，另一个 Core 实例才能接手。
/// </summary>
public sealed class GrpcSharedTokenLease(string token, FileStream lockStream) : IDisposable
{
    public string Token { get; } = token;

    public void Dispose()
    {
        lockStream.Dispose();
    }
}
