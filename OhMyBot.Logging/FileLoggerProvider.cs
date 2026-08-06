using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OhMyBot.Logging;

/// <summary>
/// 把日志按天写进 <c>log_yyyyMMdd_HHmmss.log</c>，并清理超出保留期的旧文件。
/// 行格式与 InteractiveConsoleLoggerProvider 保持一致，文件内容即控制台内容去掉颜色。
/// </summary>
[ProviderAlias("FileLogging")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly int _retainedDays;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private bool _faulted;
    private bool _disposed;

    public FileLoggerProvider(FileLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 目录建不出来属于配置错误，在进程启动时就炸掉，好过跑起来之后静默不落盘。
        _directory = Path.IsPathRooted(options.Directory)
            ? Path.GetFullPath(options.Directory)
            : Path.GetFullPath(options.Directory, AppContext.BaseDirectory);
        _retainedDays = options.RetainedDays;
        System.IO.Directory.CreateDirectory(_directory);

        lock (_gate)
        {
            OpenWriter(DateTime.Now);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var now = DateTime.Now;
                if (_writer is null || DateOnly.FromDateTime(now) != _writerDate)
                {
                    _writer?.Dispose();
                    _writer = null;
                    OpenWriter(now);
                }

                _writer!.WriteLine(line);
                _faulted = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 走 ILogger 报这个错会无限递归，只能落到 stderr；且同一段故障只报一次，避免刷屏。
                if (!_faulted)
                {
                    _faulted = true;
                    Console.Error.WriteLine($"[FileLogging] 写入日志文件失败：{exception.Message}");
                }

                _writer?.Dispose();
                _writer = null;
            }
        }
    }

    private void OpenWriter(DateTime now)
    {
        CleanupExpired(now);

        // 用 CreateNew 而不是 Append：文件名精确到秒，撞名只可能是「同一秒起来的另一个进程共用了目录」，
        // 这时要的是各写各的，而不是两个 StreamWriter 交错写同一个文件。
        // FileShare 在 Unix 上只是建议性锁，挡不住，只有 CreateNew 是跨平台确定的。
        var baseName = $"log_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
        for (var suffix = 0; ; suffix++)
        {
            var fileName = suffix == 0 ? $"{baseName}.log" : $"{baseName}_{suffix}.log";
            var path = Path.Combine(_directory, fileName);
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };
                _writerDate = DateOnly.FromDateTime(now);
                return;
            }
            // 只有「文件已存在」才换后缀重试；磁盘满、权限不足这类 IOException 必须立刻抛出去，
            // 否则每写一行都要空转 16 次建文件。
            catch (IOException) when (suffix < 16 && File.Exists(path))
            {
            }
        }
    }

    private void CleanupExpired(DateTime now)
    {
        if (_retainedDays <= 0)
        {
            return;
        }

        var cutoff = DateOnly.FromDateTime(now).AddDays(-(_retainedDays - 1));
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "log_*.log"))
        {
            // 日期解析不出来的文件一律不动：宁可留着，也不能误删别人放在这儿的东西。
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < 12
                || !DateOnly.TryParseExact(
                    name.AsSpan(4, 8),
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate)
                || fileDate >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[FileLogging] 删除过期日志 {path} 失败：{exception.Message}");
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        // 级别过滤由 ILoggerFactory 按 Logging:FileLogging:LogLevel 处理，这里不重复判断。
        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"[{timestamp}] [{FormatLevel(logLevel)}] {categoryName}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.Write(line);
        }

        private static string FormatLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => logLevel.ToString().ToLowerInvariant()
            };
        }
    }
}
