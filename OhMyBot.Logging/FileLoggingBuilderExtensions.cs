using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OhMyBot.Logging;

public static class FileLoggingBuilderExtensions
{
    /// <summary>
    /// 按 <c>Logging:FileLogging</c> 配置挂上文件日志。Core 与两个网关是独立进程、
    /// 日志互不相交（网关的投递失败 Core 侧没有记录），所以三边都要各自挂一份。
    /// </summary>
    public static ILoggingBuilder AddOhMyBotFileLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection("Logging:FileLogging").Get<FileLoggingOptions>()
            ?? new FileLoggingOptions();
        if (!options.Enabled)
        {
            return builder;
        }

        // 在这里就把目录建好、文件开好：路径配错要在 Program.cs 这一行炸，而不是等第一条日志。
        var provider = new FileLoggerProvider(options);
        builder.Services.AddSingleton<ILoggerProvider>(_ => provider);
        return builder;
    }
}
