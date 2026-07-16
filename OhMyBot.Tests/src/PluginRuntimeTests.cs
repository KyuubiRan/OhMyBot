using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Host.Plugins;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Tests;

[TestClass]
public sealed class PluginRuntimeTests
{
    [TestMethod]
    public void DiscoveryReadsPluginMetadataWithoutManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "ohmybot-plugin-discovery-" + Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "Fixture");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            File.Copy(typeof(PluginRuntimeTests).Assembly.Location, Path.Combine(pluginDirectory, "Plugin.dll"));
            foreach (var dependency in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            {
                var destination = Path.Combine(pluginDirectory, Path.GetFileName(dependency));
                if (!File.Exists(destination))
                {
                    File.Copy(dependency, destination);
                }
            }

            var descriptors = PluginDiscovery.Discover(root);
            Assert.HasCount(1, descriptors);
            var descriptor = descriptors[0];
            Assert.AreEqual("com.ohmybot.tests.fixture", descriptor.Metadata.Id);
            Assert.AreEqual("1.2.3", descriptor.Metadata.Version);
            Assert.AreEqual(321, descriptor.Metadata.LoadPriority);
            Assert.AreEqual(PluginSupportedPlatforms.All, descriptor.Metadata.SupportedPlatforms);
            Assert.AreEqual(typeof(PluginRuntimeFixture).FullName, descriptor.EntryTypeName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DiscoverySkipsMalformedPluginAndKeepsValidDescriptors()
    {
        var root = Path.Combine(Path.GetTempPath(), "ohmybot-plugin-fail-soft-discovery-" + Guid.NewGuid().ToString("N"));
        var validDirectory = Path.Combine(root, "Valid");
        var brokenDirectory = Path.Combine(root, "Broken");
        Directory.CreateDirectory(validDirectory);
        Directory.CreateDirectory(brokenDirectory);
        try
        {
            CopyFixturePlugin(validDirectory);
            File.WriteAllText(Path.Combine(brokenDirectory, "Plugin.dll"), "not a managed assembly");

            var result = PluginDiscovery.DiscoverDetailed(root);

            Assert.HasCount(1, result.Descriptors);
            Assert.AreEqual("com.ohmybot.tests.fixture", result.Descriptors[0].Metadata.Id);
            Assert.HasCount(1, result.Failures);
            Assert.AreEqual(brokenDirectory, result.Failures[0].SourceDirectory);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Failures[0].Error));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void PluginPlatformDeclarationCapsCommandTree()
    {
        var provider = new LeasedCommandProvider(
            "com.ohmybot.tests.platform",
            PluginSupportedPlatforms.Telegram,
            new StaticCommandProvider(
            [
                new CommandDslNode
                {
                    Name = "platform-test",
                    Description = "Platform test.",
                    Usage = "/platform-test",
                    SupportPlatforms = SupportedPlatforms.All
                }
            ]),
            new PluginInvocationGate(),
            []);

        var node = provider.GetNodes().Single();

        Assert.AreEqual(SupportedPlatforms.Telegram, node.SupportPlatforms);
    }

    [TestMethod]
    public async Task PluginCallbackRejectsUndeclaredPlatform()
    {
        var handler = new LeasedCallbackHandler(
            "com.ohmybot.tests.platform",
            PluginSupportedPlatforms.Telegram,
            new ThrowingCallbackHandler(),
            new PluginInvocationGate(),
            []);
        var request = new OhMyBot.Contracts.Grpc.CommandRequest
        {
            Platform = OhMyBot.Contracts.Grpc.BotPlatform.Qq,
            UserId = "qq-user",
            ChatId = "qq-chat"
        };
        var context = new CommandContext(
            request,
            new ResolvedIdentity(
                1,
                OhMyBot.Contracts.Grpc.UserPrivilege.User,
                request.Platform,
                request.UserId),
            TimeProvider.System.GetTimestamp(),
            CancellationToken.None);

        var response = await handler.ExecuteAsync(
            "test",
            context,
            new CallbackAction("test", "hash", 1, request.ChatId, request.UserId, true, "{}"),
            "message");

        Assert.AreEqual("UnsupportedPlatform", response.ErrorCode);
    }

    [TestMethod]
    public async Task ManagerLoadsAndReloadsCollectiblePluginGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), "ohmybot-plugin-manager-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "Plugins");
        var pluginDirectory = Path.Combine(pluginRoot, "Fixture");
        var brokenDirectory = Path.Combine(pluginRoot, "Broken");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(brokenDirectory);
        CopyFixturePlugin(pluginDirectory);
        File.WriteAllText(Path.Combine(brokenDirectory, "Plugin.dll"), "not a managed assembly");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        await using var provider = services.BuildServiceProvider();
        var commandRegistry = new PlatformCommandDslRegistry([]);
        var callbackRegistry = new PluginCallbackRegistry();
        var taskRegistry = new ManagedTaskRegistry([]);
        var adminRegistry = new PluginAdminCommandRegistry();
        var notificationRegistry = new OhMyBot.Core.Commanding.Notifications.PluginNotificationSourceRegistry();
        var routeStore = new RouteStore(
            commandRegistry,
            Options.Create(new RouteOptions { Path = Path.Combine(root, "route.json") }),
            NullLogger<RouteStore>.Instance);
        var manager = new PluginManager(
            provider,
            commandRegistry,
            callbackRegistry,
            taskRegistry,
            adminRegistry,
            notificationRegistry,
            routeStore,
            new FakeRouteChangePublisher(),
            Options.Create(new PluginRuntimeOptions
            {
                PluginPath = pluginRoot,
                ShadowPath = Path.Combine(root, ".plugin-cache")
            }),
            NullLogger<PluginManager>.Instance);

        try
        {
            await manager.StartAsync(CancellationToken.None);
            var firstSnapshot = manager.GetPlugins();
            Assert.HasCount(2, firstSnapshot);
            var first = firstSnapshot.Single(item => item.Id == "com.ohmybot.tests.fixture");
            Assert.AreEqual(PluginState.Active, first.State);
            Assert.IsGreaterThan(0L, first.Generation);
            var broken = firstSnapshot.Single(item => item.Id == "invalid:Broken");
            Assert.AreEqual(PluginState.Faulted, broken.State);
            Assert.IsFalse(string.IsNullOrWhiteSpace(broken.LastError));

            var reload = await manager.ReloadAsync(first.Id);
            Assert.IsTrue(reload.Success, reload.Message);
            var secondSnapshot = manager.GetPlugins();
            Assert.HasCount(2, secondSnapshot);
            var second = secondSnapshot.Single(item => item.Id == first.Id);
            Assert.IsGreaterThan(first.Generation, second.Generation);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DependenciesOverrideLoadPriorityAndPeersUseDescendingPriority()
    {
        var common = Descriptor("com.example.common", priority: 0);
        var high = Descriptor("com.example.high", priority: 500);
        var dependent = Descriptor(
            "com.example.dependent",
            priority: 1000,
            new PluginDependency(common.Metadata.Id, "[1.0.0,2.0.0)", true));

        var order = PluginManager.ValidateAndSort([dependent, common, high]);
        CollectionAssert.AreEqual(
            new[] { high.Metadata.Id, common.Metadata.Id, dependent.Metadata.Id },
            order.Select(item => item.Metadata.Id).ToArray());
    }

    [TestMethod]
    public void ValidationFaultsBrokenDependencyChainWithoutBlockingIndependentPlugin()
    {
        var missingDependency = Descriptor(
            "com.example.broken",
            priority: 500,
            new PluginDependency("com.example.missing", "[1.0.0,2.0.0)", true));
        var dependent = Descriptor(
            "com.example.dependent",
            priority: 1000,
            new PluginDependency(missingDependency.Metadata.Id, "[1.0.0,2.0.0)", true));
        var independent = Descriptor("com.example.independent", priority: 1);

        var result = PluginManager.ValidateAndSortFailSoft([dependent, missingDependency, independent]);

        CollectionAssert.AreEqual(
            new[] { independent.Metadata.Id },
            result.LoadOrder.Select(item => item.Metadata.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { missingDependency.Metadata.Id, dependent.Metadata.Id },
            result.Failures.Select(item => item.Descriptor.Metadata.Id).ToArray());
    }

    [TestMethod]
    public void ValidationRejectsDuplicateAssemblyIdentityWithoutBlockingIndependentPlugin()
    {
        var first = Descriptor("com.example.first", priority: 10) with { AssemblyName = "Duplicate.Plugin" };
        var second = Descriptor("com.example.second", priority: 20) with { AssemblyName = "Duplicate.Plugin" };
        var independent = Descriptor("com.example.independent", priority: 1);

        var result = PluginManager.ValidateAndSortFailSoft([first, second, independent]);

        CollectionAssert.AreEqual(
            new[] { independent.Metadata.Id },
            result.LoadOrder.Select(item => item.Metadata.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { first.Metadata.Id, second.Metadata.Id },
            result.Failures.Select(item => item.Descriptor.Metadata.Id).ToArray());
        Assert.IsTrue(result.Failures.All(item => item.Error.Contains("AssemblyName", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DirectPluginReferenceUsesDependencyTypeIdentityAndReloadsDependentClosure()
    {
        var root = Path.Combine(Path.GetTempPath(), "ohmybot-plugin-dependency-runtime-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "Plugins");
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "PluginFixtures");
        var aDirectory = Path.Combine(pluginRoot, "A");
        var bDirectory = Path.Combine(pluginRoot, "B");
        Directory.CreateDirectory(aDirectory);
        Directory.CreateDirectory(bDirectory);
        File.Copy(Path.Combine(fixtureRoot, "A", "Plugin.dll"), Path.Combine(aDirectory, "Plugin.dll"));
        File.Copy(Path.Combine(fixtureRoot, "B", "Plugin.dll"), Path.Combine(bDirectory, "Plugin.dll"));

        var aAssemblyName = AssemblyName.GetAssemblyName(Path.Combine(aDirectory, "Plugin.dll")).Name;
        var bAssemblyName = AssemblyName.GetAssemblyName(Path.Combine(bDirectory, "Plugin.dll")).Name;
        Assert.AreEqual("OhMyBot.Tests.PluginDependencyA", aAssemblyName);
        Assert.AreEqual("OhMyBot.Tests.PluginDependencyB", bAssemblyName);
        Assert.AreNotEqual(aAssemblyName, bAssemblyName);
        Assert.IsFalse(File.Exists(Path.Combine(aDirectory, "OhMyBot.Tests.PluginDependencyB.dll")));

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        var commandRegistry = new PlatformCommandDslRegistry([]);
        var callbackRegistry = new PluginCallbackRegistry();
        var taskRegistry = new ManagedTaskRegistry([]);
        var adminRegistry = new PluginAdminCommandRegistry();
        var notificationRegistry = new OhMyBot.Core.Commanding.Notifications.PluginNotificationSourceRegistry();
        var routeStore = new RouteStore(
            commandRegistry,
            Options.Create(new RouteOptions { Path = Path.Combine(root, "route.json") }),
            NullLogger<RouteStore>.Instance);
        var manager = new PluginManager(
            provider,
            commandRegistry,
            callbackRegistry,
            taskRegistry,
            adminRegistry,
            notificationRegistry,
            routeStore,
            new FakeRouteChangePublisher(),
            Options.Create(new PluginRuntimeOptions
            {
                PluginPath = pluginRoot,
                ShadowPath = Path.Combine(root, ".plugin-cache")
            }),
            NullLogger<PluginManager>.Instance);

        try
        {
            await manager.StartAsync(CancellationToken.None);
            var first = manager.GetPlugins().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            Assert.HasCount(2, first);
            Assert.IsTrue(
                first.ContainsKey("com.ohmybot.tests.dependency-a"),
                string.Join(" | ", first.Values.Select(item => $"{item.Id}: {item.State}: {item.LastError}")));
            Assert.AreEqual(PluginState.Active, first["com.ohmybot.tests.dependency-b"].State);
            Assert.AreEqual(PluginState.Active, first["com.ohmybot.tests.dependency-a"].State);

            var reload = await manager.ReloadAsync("com.ohmybot.tests.dependency-b");
            Assert.IsTrue(reload.Success, reload.Message);
            CollectionAssert.AreEqual(
                new[] { "com.ohmybot.tests.dependency-b", "com.ohmybot.tests.dependency-a" },
                reload.ReloadedPluginIds.ToArray());

            var second = manager.GetPlugins().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            Assert.IsGreaterThan(
                first["com.ohmybot.tests.dependency-b"].Generation,
                second["com.ohmybot.tests.dependency-b"].Generation);
            Assert.IsGreaterThan(
                first["com.ohmybot.tests.dependency-a"].Generation,
                second["com.ohmybot.tests.dependency-a"].Generation);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    private static PluginDescriptor Descriptor(
        string id,
        int priority,
        params PluginDependency[] dependencies)
    {
        return new PluginDescriptor(
            "/tmp/" + id,
            "/tmp/" + id + "/Plugin.dll",
            id + ".Plugin",
            id + ".Assembly",
            new PluginMetadata(
                id,
                id,
                "1.0.0",
                "[1.0.0,2.0.0)",
                priority,
                PluginSupportedPlatforms.All,
                dependencies));
    }

    private static void CopyFixturePlugin(string pluginDirectory)
    {
        File.Copy(typeof(PluginRuntimeTests).Assembly.Location, Path.Combine(pluginDirectory, "Plugin.dll"));
        foreach (var dependency in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var destination = Path.Combine(pluginDirectory, Path.GetFileName(dependency));
            if (!File.Exists(destination))
            {
                File.Copy(dependency, destination);
            }
        }
    }

    private sealed class FakeRouteChangePublisher : IRouteChangePublisher
    {
        public Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StaticCommandProvider(IReadOnlyList<CommandDslNode> nodes)
        : IPlatformCommandDslProvider
    {
        public IEnumerable<CommandDslNode> GetNodes() => nodes;
    }

    private sealed class ThrowingCallbackHandler : IPluginCallbackHandler
    {
        public IReadOnlyCollection<string> ActionTypes { get; } = ["test"];

        public Task<OhMyBot.Contracts.Grpc.CommandResponse> ExecuteAsync(
            string actionType,
            CommandContext context,
            CallbackAction action,
            string editMessageId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Unsupported platform must be rejected before plugin execution.");
    }
}

[OhMyBotPlugin(
    "com.ohmybot.tests.fixture",
    "Fixture",
    "1.2.3",
    CoreApi = "[1.0.0,2.0.0)",
    LoadPriority = 321,
    SupportedPlatforms = PluginSupportedPlatforms.All)]
public sealed class PluginRuntimeFixture : BasicPlugin;
