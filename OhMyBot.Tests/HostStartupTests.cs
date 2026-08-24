using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyBot.Core;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Host;
using OhMyBot.Core.Host.Plugins;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Plugins;
using OhMyBot.Core.Infrastructure.Terminal;

namespace OhMyBot.Tests;

[TestClass]
public sealed class HostStartupTests
{
    [TestMethod]
    public void NormalStartupDoesNotEnterRemoteConsoleMode()
    {
        var parsed = HostStartupArguments.Parse(["--environment", "Development"]);

        Assert.IsFalse(parsed.RemoteConsoleRequested);
        CollectionAssert.AreEqual(new[] { "--environment", "Development" }, parsed.HostArguments);
    }

    [TestMethod]
    public void RemoteConsoleModeIsExplicitAndRemovedFromHostConfigurationArguments()
    {
        var parsed = HostStartupArguments.Parse(["--REMOTE-CONSOLE", "--environment", "Development"]);

        Assert.IsTrue(parsed.RemoteConsoleRequested);
        CollectionAssert.AreEqual(new[] { "--environment", "Development" }, parsed.HostArguments);
    }

    [TestMethod]
    public void ConsoleRendererStartsBeforeDatabaseMigration()
    {
        var services = new ServiceCollection();
        services.AddOhMyBotCoreServices();

        var hostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        var rendererIndex = Array.IndexOf(hostedServices, typeof(InteractiveConsoleRendererHostedService));
        var migrationIndex = Array.IndexOf(hostedServices, typeof(DatabaseMigrationHostedService));

        Assert.IsGreaterThanOrEqualTo(0, rendererIndex);
        Assert.IsTrue(rendererIndex < migrationIndex);
    }

    [TestMethod]
    public void PluginRuntimeServicesResolveWithoutCircularDependency()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddOhMyBotCoreServices();
        services.AddOhMyBotPluginRuntime(configuration);

        using var provider = services.BuildServiceProvider();
        var commandRegistry = provider.GetRequiredService<PlatformCommandDslRegistry>();
        var pluginManager = provider.GetRequiredService<IPluginManager>();

        Assert.IsNotNull(commandRegistry);
        Assert.IsInstanceOfType<PluginManager>(pluginManager);
    }

    [TestMethod]
    public async Task ConsoleQueueReplaysLogsWrittenBeforeRendererStarts()
    {
        var queue = new InteractiveConsoleOutputQueue();
        var expected = new InteractiveConsoleOutputItem([new ConsoleTextSegment("starting")]);
        Assert.IsTrue(queue.TryEnqueue(expected));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var enumerator = queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.IsTrue(await enumerator.MoveNextAsync());
        Assert.AreSame(expected, enumerator.Current);
    }

    [TestMethod]
    public async Task ConsoleSubscriptionReplaysLatestHundredAndPagesOlderLogs()
    {
        var queue = new InteractiveConsoleOutputQueue();
        await using var existingReader = queue.Subscribe(0);
        for (var index = 0; index < 150; index++)
        {
            queue.TryEnqueue(new InteractiveConsoleOutputItem([new ConsoleTextSegment($"log-{index}")]));
        }

        await using var subscription = queue.Subscribe(100);

        Assert.HasCount(100, subscription.InitialHistory);
        Assert.AreEqual("log-50", subscription.InitialHistory[0].Segments[0].Text);
        Assert.AreEqual("log-149", subscription.InitialHistory[^1].Segments[0].Text);

        var firstOlderPage = subscription.ReadOlder(30);
        Assert.HasCount(30, firstOlderPage);
        Assert.AreEqual("log-20", firstOlderPage[0].Segments[0].Text);
        Assert.AreEqual("log-49", firstOlderPage[^1].Segments[0].Text);

        var secondOlderPage = subscription.ReadOlder(100);
        Assert.HasCount(20, secondOlderPage);
        Assert.AreEqual("log-0", secondOlderPage[0].Segments[0].Text);
        Assert.AreEqual("log-19", secondOlderPage[^1].Segments[0].Text);
        Assert.IsEmpty(subscription.ReadOlder(100));
    }
}
