using Microsoft.Extensions.Logging;

namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleLoggerProvider(InteractiveConsoleState consoleState) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new InteractiveConsoleLogger(consoleState, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class InteractiveConsoleLogger(
        InteractiveConsoleState consoleState,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is not LogLevel.None;
        }

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
            var level = FormatLevel(logLevel);
            var suffix = $" {categoryName}: {message}";
            if (exception is not null)
            {
                suffix += Environment.NewLine + exception;
            }

            var (foregroundColor, backgroundColor) = GetColors(logLevel);
            consoleState.WriteSegmentsLine(
                new ConsoleTextSegment($"[{timestamp}] "),
                new ConsoleTextSegment($"[{level}]", foregroundColor, backgroundColor),
                new ConsoleTextSegment(suffix));
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

        private static (ConsoleColor? Foreground, ConsoleColor? Background) GetColors(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => (ConsoleColor.DarkGray, null),
                LogLevel.Information => (ConsoleColor.Green, null),
                LogLevel.Warning => (ConsoleColor.Yellow, null),
                LogLevel.Error => (ConsoleColor.Red, null),
                LogLevel.Critical => (ConsoleColor.White, ConsoleColor.Red),
                _ => (null, null)
            };
        }
    }
}
