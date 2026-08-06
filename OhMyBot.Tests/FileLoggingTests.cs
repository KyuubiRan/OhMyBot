using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyBot.Logging;

namespace OhMyBot.Tests;

[TestClass]
public sealed class FileLoggingTests
{
    private static string CreateTempDirectory()
        => Path.Combine(Path.GetTempPath(), "ohmybot-file-logging-" + Guid.NewGuid().ToString("N"));

    private static string LogFileName(DateTime day)
        => $"log_{day.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.log";

    /// <summary>
    /// 落盘的行必须和 InteractiveConsoleLoggerProvider 的输出对得上：
    /// 这个能力的意义就是「控制台上看到什么，文件里就有什么」，格式一旦漂移，
    /// 按 errorId 在文件里搜就对不上控制台/journal 里的那一条。
    /// </summary>
    [TestMethod]
    public void WritesConsoleFormattedLineToTimestampedFile()
    {
        var root = CreateTempDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(new FileLoggingOptions { Directory = root }))
            {
                provider.CreateLogger("OhMyBot.Sample")
                    .LogWarning("命令执行失败。errorId={ErrorId}", "abc123");
            }

            var files = Directory.GetFiles(root, "log_*.log");
            Assert.AreEqual(1, files.Length);
            Assert.AreEqual(LogFileName(DateTime.Now), Path.GetFileName(files[0]));

            var line = File.ReadAllLines(files[0]).Single();
            StringAssert.EndsWith(line, "[warn] OhMyBot.Sample: 命令执行失败。errorId=abc123");
            StringAssert.Matches(line, new System.Text.RegularExpressions.Regex(
                @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] "));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 异常必须整段落盘。Core 和网关都只把 errorId 回给用户，堆栈只存在于日志里，
    /// 丢了堆栈这条日志就失去了全部排查价值。
    /// </summary>
    [TestMethod]
    public void WritesExceptionDetailsBelowMessage()
    {
        var root = CreateTempDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(new FileLoggingOptions { Directory = root }))
            {
                provider.CreateLogger("OhMyBot.Sample")
                    .LogError(new InvalidOperationException("上游 API 拒绝"), "发送失败");
            }

            var content = File.ReadAllText(Directory.GetFiles(root, "log_*.log").Single());
            StringAssert.Contains(content, "[fail] OhMyBot.Sample: 发送失败");
            StringAssert.Contains(content, "System.InvalidOperationException: 上游 API 拒绝");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// RetainedDays=7 的语义是「含当天在内保留 7 天」：第 7 天前的必须留着，
    /// 第 8 天前的必须删掉。写错成 8 天或 6 天，nanopi 上的磁盘占用和可回溯范围都会跟预期不符。
    /// </summary>
    [TestMethod]
    public void DeletesOnlyFilesOlderThanRetentionWindow()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTime.Now;
            var newest = LogFileName(now.AddDays(-6));
            var oldest = LogFileName(now.AddDays(-7));
            File.WriteAllText(Path.Combine(root, newest), "keep");
            File.WriteAllText(Path.Combine(root, oldest), "drop");

            using var provider = new FileLoggerProvider(
                new FileLoggingOptions { Directory = root, RetainedDays = 7 });

            Assert.IsTrue(File.Exists(Path.Combine(root, newest)));
            Assert.IsFalse(File.Exists(Path.Combine(root, oldest)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// RetainedDays 小于等于 0 是「不清理」的逃生口，用于临时保全现场，不能被当成 0 天全删。
    /// </summary>
    [TestMethod]
    public void KeepsEverythingWhenRetentionDisabled()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            var ancient = LogFileName(DateTime.Now.AddYears(-1));
            File.WriteAllText(Path.Combine(root, ancient), "keep");

            using var provider = new FileLoggerProvider(
                new FileLoggingOptions { Directory = root, RetainedDays = 0 });

            Assert.IsTrue(File.Exists(Path.Combine(root, ancient)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 清理只认自己写出来的命名。日志目录是人会手动放东西的地方（导出的现场、压缩包），
    /// 名字解析不出日期就必须放过，否则这个功能会变成定时删文件的地雷。
    /// </summary>
    [TestMethod]
    public void NeverDeletesFilesItCannotDate()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            string[] foreign = ["notes.txt", "log_backup.log", "log_notadate_1.log"];
            foreach (var name in foreign)
            {
                File.WriteAllText(Path.Combine(root, name), "keep");
            }

            using var provider = new FileLoggerProvider(
                new FileLoggingOptions { Directory = root, RetainedDays = 1 });

            foreach (var name in foreign)
            {
                Assert.IsTrue(File.Exists(Path.Combine(root, name)), name);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 同一秒启动的两个进程若被配到同一个目录，必须各拿一个文件；
    /// 两个 StreamWriter 交错写同一个文件会把两边的日志都毁掉。
    /// </summary>
    [TestMethod]
    public void SecondProviderInSameDirectoryTakesItsOwnFile()
    {
        var root = CreateTempDirectory();
        try
        {
            using var first = new FileLoggerProvider(new FileLoggingOptions { Directory = root });
            using var second = new FileLoggerProvider(new FileLoggingOptions { Directory = root });
            first.CreateLogger("A").LogInformation("第一个进程");
            second.CreateLogger("B").LogInformation("第二个进程");

            var files = Directory.GetFiles(root, "log_*.log");
            Assert.AreEqual(2, files.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 配置节名一旦写错，表现是「什么都不报，就是没有日志文件」——最难发现的一类故障。
    /// 这条固定住 Logging:FileLogging 这个路径，以及它确实经由 ILoggerFactory 生效。
    /// </summary>
    [TestMethod]
    public void BindsLoggingFileLoggingSectionThroughLoggerFactory()
    {
        var root = CreateTempDirectory();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:FileLogging:Directory"] = root,
                    ["Logging:FileLogging:RetainedDays"] = "3"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddOhMyBotFileLogging(configuration));

            using (var serviceProvider = services.BuildServiceProvider())
            {
                serviceProvider.GetRequiredService<ILogger<FileLoggingTests>>().LogInformation("启动完成");
            }

            var content = File.ReadAllText(Directory.GetFiles(root, "log_*.log").Single());
            StringAssert.Contains(content, "启动完成");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Enabled=false 必须彻底不碰磁盘（连目录都不建），否则「关掉」就只是关了一半。
    /// </summary>
    [TestMethod]
    public void DisabledConfigurationTouchesNothingOnDisk()
    {
        var root = CreateTempDirectory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:FileLogging:Enabled"] = "false",
                ["Logging:FileLogging:Directory"] = root
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddOhMyBotFileLogging(configuration));

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<ILogger<FileLoggingTests>>().LogInformation("启动完成");

        Assert.IsFalse(Directory.Exists(root));
    }
}
