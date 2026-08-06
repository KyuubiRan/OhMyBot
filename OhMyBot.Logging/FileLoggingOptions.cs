namespace OhMyBot.Logging;

/// <summary>
/// 绑定自 <c>Logging:FileLogging</c>。与该节下的 <c>LogLevel</c> 共存：
/// 级别过滤由 ILoggerFactory 按 ProviderAlias 处理，这里只管落盘行为。
/// </summary>
public sealed class FileLoggingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 相对路径按进程输出目录解析。三个服务各有自己的 WorkingDirectory，
    /// 所以默认值相同也不会互相覆盖；publish.sh 的 PROTECTED_PATTERNS 已排除 logs/。
    /// </summary>
    public string Directory { get; set; } = "logs";

    /// <summary>
    /// 保留最近几天（含当天）的日志文件，小于等于 0 表示不清理。
    /// </summary>
    public int RetainedDays { get; set; } = 7;
}
