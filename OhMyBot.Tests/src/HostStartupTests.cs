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
}
