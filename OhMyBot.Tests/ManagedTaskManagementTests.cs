using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Host.Plugins;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Infrastructure.ScheduledTasks;
using OhMyBot.Plugin.Commanding.SignIn;

namespace OhMyBot.Tests;

[TestClass]
public sealed class ManagedTaskManagementTests
{
    [TestMethod]
    public async Task ConcurrentStartsRunOnceAndCancelDoesNotDisableSchedule()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var task = new TestTask("test", async token =>
        {
            Interlocked.Increment(ref invocations);
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var executions = new List<Task>();
        var accepted = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            var ok = task.TryStart(out var execution);
            if (ok) lock (executions) executions.Add(execution);
            return ok;
        })));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, accepted.Count(value => value));
        Assert.AreEqual(1, invocations);
        Assert.IsTrue(task.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => executions.Single());
        Assert.IsTrue(task.Enabled);
        Assert.IsFalse(task.IsRunning);
        Assert.AreEqual(ManagedTaskOutcome.Canceled, task.Status.Outcome);
        Assert.IsNotNull(task.Status.LastFinishedAt);
        Assert.IsNull(task.Status.LastError);
    }

    [TestMethod]
    public async Task DisablingDoesNotCancelCurrentExecutionAndManualRunWorksWhileDisabled()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new TestTask("test", async token => { started.SetResult(); await finish.Task.WaitAsync(token); });
        Assert.IsTrue(task.TryStart(out var execution));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        task.Enabled = false;
        Assert.IsTrue(task.IsRunning);
        Assert.IsFalse(task.Status.CancellationRequested);
        finish.SetResult();
        await execution;
        Assert.AreEqual(ManagedTaskOutcome.Succeeded, task.Status.Outcome);
        var disabled = new TestTask("disabled", _ => Task.CompletedTask) { Enabled = false };
        Assert.IsTrue(disabled.TryStart(out var manual));
        await manual;
        Assert.IsFalse(disabled.Enabled);
    }

    [TestMethod]
    public async Task IsolatedSignInModuleFailureMarksTaskFailedWithoutLosingOtherResults()
    {
        var completedOther = false;
        var executor = new SignInExecutor(NullLogger<SignInExecutor>.Instance, retryDelay: TimeSpan.Zero);
        var task = new TestTask("batch", async token =>
        {
            await executor.RunModulesAsync([
                new SignInModule("broken", _ => throw new InvalidOperationException("upstream failed token=private-token")),
                new SignInModule("other", _ => { completedOther = true; return Task.FromResult<IReadOnlyList<string>>(["ok"]); })
            ], token);
        });
        await task.ExecuteAsync();
        Assert.IsTrue(completedOther);
        Assert.AreEqual(ManagedTaskOutcome.Failed, task.Status.Outcome);
        Assert.AreEqual(1, task.Status.FailureCount);
        Assert.IsNotNull(task.Status.LastFinishedAt);
        Assert.IsNull(task.Status.LastSucceededAt);
        Assert.Contains("broken", task.Status.LastErrorDetails!);
        Assert.Contains(nameof(InvalidOperationException), task.Status.LastErrorDetails!);
        Assert.IsNotNull(task.Status.LastErrorId);
    }

    [TestMethod]
    public async Task PersistedStateRetainsDisableFailureAndErrorAfterRestartAndLaterSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ohmybot-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = new TestTask("persistent", _ => throw new InvalidOperationException("failed password=private-value"));
            var registry = new ManagedTaskRegistry([original], Store(directory));
            var task = registry.Tasks.Single();
            task.Enabled = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync());
            var saved = File.ReadAllText(Path.Combine(directory, ManagedTaskStateStore.FileName));
            Assert.DoesNotContain("private-value", saved);
            Assert.Contains("REDACTED", saved);
            var restarted = new ManagedTaskRegistry([new TestTask("persistent", _ => Task.CompletedTask)], Store(directory));
            task = restarted.Tasks.Single();
            Assert.IsFalse(task.Enabled);
            Assert.AreEqual(ManagedTaskOutcome.Failed, task.Status.Outcome);
            Assert.IsNotNull(task.Status.LastStartedAt);
            Assert.IsNotNull(task.Status.LastFailedAt);
            Assert.IsNotNull(task.Status.LastFinishedAt);
            await task.ExecuteAsync();
            Assert.AreEqual(ManagedTaskOutcome.Succeeded, task.Status.Outcome);
            Assert.IsNotNull(task.Status.LastErrorDetails);
            Assert.IsNotNull(task.Status.LastSucceededAt);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void PreviousRunningStateBecomesInterruptedWithoutInventingCompletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ohmybot-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Store(directory).Save("interrupted", true, new ManagedTaskStatus(ManagedTaskOutcome.Running, false,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null, null, 0));
            var registry = new ManagedTaskRegistry([new TestTask("interrupted", _ => Task.CompletedTask)], Store(directory));
            Assert.AreEqual(ManagedTaskOutcome.Interrupted, registry.Tasks.Single().Status.Outcome);
            Assert.IsFalse(registry.Tasks.Single().IsRunning);
            Assert.IsNull(registry.Tasks.Single().Status.LastFinishedAt);
            Assert.IsNull(registry.Tasks.Single().Status.LastSucceededAt);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ConcurrentDisableAndCompletionRemainDisabledAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ohmybot-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var name = "concurrent-" + attempt;
                var registry = new ManagedTaskRegistry([new TestTask(name, _ => finish.Task)], Store(directory));
                var task = registry.Tasks.Single();
                Assert.IsTrue(task.TryStart(out var execution));
                await Task.WhenAll(Task.Run(() => task.Enabled = false), Task.Run(() => finish.SetResult()));
                await execution;
                var restarted = new ManagedTaskRegistry([new TestTask(name, _ => Task.CompletedTask)], Store(directory));
                Assert.IsFalse(restarted.Tasks.Single().Enabled);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ReloadedTaskCannotBeChangedOrOverwrittenByPreviousRegistration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ohmybot-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = Store(directory);
            var registry = new ManagedTaskRegistry([], store);
            registry.RegisterPlugin("plugin", [new TestTask("same", _ => Task.CompletedTask)]);
            Assert.IsTrue(registry.TryGetRegistration("same", out var old));
            registry.UnregisterPlugin("plugin");
            registry.RegisterPlugin("plugin", [new TestTask("same", _ => Task.CompletedTask)]);
            var replacement = registry.Tasks.Single();
            replacement.Enabled = false;
            Assert.IsFalse(registry.TryControl(old, task => { task.Enabled = true; return true; }));
            old.Task.Enabled = true;
            Assert.IsFalse(replacement.Enabled);
            var restarted = new ManagedTaskRegistry([new TestTask("same", _ => Task.CompletedTask)], Store(directory));
            Assert.IsFalse(restarted.Tasks.Single().Enabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task OwnerPanelHasPluginMetadataAndExecutionButtonsAndErrorsAreRedacted()
    {
        await using var harness = new Harness();
        var task = new TestTask("plugin:task", _ => throw new InvalidOperationException(
            "https://user:pass@example.com/check?token=url-secret\nAuthorization: Bearer bearer-secret\npassword=json-secret\n\"stoken_v2\":\"cookie-secret\"\nbot123:telegram-secret"));
        harness.Registry.RegisterPlugin("test.plugin", [task]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync());
        var list = await harness.Provider.BuildListAsync(Context());
        Assert.Contains("test.plugin", list.TgText());
        var details = await harness.ClickAsync(list.TgButtonRows().First().Buttons.First().Payload);
        CollectionAssert.IsSubsetOf(new[] { "执行一次", "禁用调度", "查看报错详情", "刷新状态", "返回列表" }, details.TgButtonTexts().ToArray());
        Assert.Contains("最后开始", details.TgText());
        Assert.Contains("最后报错", details.TgText());
        var error = await harness.ClickAsync(details.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "查看报错详情").Payload);
        Assert.Contains(nameof(InvalidOperationException), error.TgText());
        foreach (var secret in new[] { "user:pass", "url-secret", "bearer-secret", "json-secret", "cookie-secret", "telegram-secret" })
            Assert.DoesNotContain(secret, error.TgText());
        Assert.AreEqual("panel-message", error.Telegram.Messages[0].EditMessageId);
    }

    [TestMethod]
    public async Task CallbackRechecksOwnerPrivilegeAndRejectsOtherChat()
    {
        await using var harness = new Harness();
        var task = new TestTask("test", _ => Task.CompletedTask);
        harness.Registry.RegisterPlugin("test.plugin", [task]);
        var list = await harness.Provider.BuildListAsync(Context());
        var details = await harness.ClickAsync(list.TgButtonRows().First().Buttons.First().Payload);
        var payload = details.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "执行一次").Payload;
        var differentChat = await harness.ClickAsync(payload, chat: "different-chat");
        Assert.AreEqual(1, differentChat.Code);
        Assert.IsNull(task.Status.LastStartedAt);
        harness.Identity.Privilege = UserPrivilege.Admin;
        var downgraded = await harness.ClickAsync(payload);
        Assert.AreEqual("TaskManagementForbidden", downgraded.ErrorCode);
        Assert.IsNull(task.Status.LastStartedAt);
        Assert.AreEqual(UserPrivilege.Owner, harness.Provider.GetNodes().Single().RequiredPrivilege);
    }

    [TestMethod]
    public async Task StaleButtonCannotControlReloadedTaskWithSameName()
    {
        await using var harness = new Harness();
        harness.Registry.RegisterPlugin("plugin", [new TestTask("same", _ => Task.CompletedTask)]);
        var root = await harness.Provider.BuildListAsync(Context());
        var detail = await harness.ClickAsync(root.TgButtonRows().First().Buttons.First().Payload);
        var payload = detail.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "执行一次").Payload;
        harness.Registry.UnregisterPlugin("plugin");
        var replacement = new TestTask("same", _ => Task.CompletedTask);
        harness.Registry.RegisterPlugin("plugin", [replacement]);
        var response = await harness.ClickAsync(payload);
        Assert.Contains("重新加载", response.TgText());
        Assert.IsNull(replacement.LastStartedAt);
    }

    [TestMethod]
    public async Task ManualExecutionHonorsPluginDrainLease()
    {
        var gate = new PluginInvocationGate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new TestTask("test", async token => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var leased = new LeasedManagedTask("plugin", inner, gate);
        Assert.IsTrue(leased.TryStart(out var execution));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var draining = gate.BeginDrainAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.IsFalse(draining.IsCompleted);
        Assert.IsFalse(leased.TryStart(out _));
        Assert.IsTrue(leased.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
        await draining;
        Assert.IsFalse(leased.TryStart(out _));
    }

    [TestMethod]
    public async Task ListGroupsPluginsAndNumberButtonsNavigateWithoutRepeatingTaskNames()
    {
        await using var harness = new Harness();
        harness.Registry.RegisterPlugin("beta", Enumerable.Range(1, 9)
            .Select(i => new TestTask($"beta:task-{i:00}", _ => Task.CompletedTask)));
        harness.Registry.RegisterPlugin("alpha", Enumerable.Range(1, 12)
            .Select(i => new TestTask($"alpha:task-{i:00}", _ => Task.CompletedTask)));

        var first = await harness.Provider.BuildListAsync(Context());
        Assert.Contains("第 1/3 页", first.TgText());
        Assert.Contains("[alpha]\n1. task-01", first.TgText());
        Assert.AreEqual(1, first.TgText().Split("[alpha]").Length - 1);
        Assert.DoesNotContain("alpha:", first.TgText());
        Assert.DoesNotContain("[beta]", first.TgText());
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5" },
            first.TgButtonRows().First().Buttons.Select(b => b.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "6", "7", "8", "9", "10" },
            first.TgButtonRows().ElementAt(1).Buttons.Select(b => b.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "刷新", "下一页" },
            first.TgButtonRows().Last().Buttons.Select(b => b.Text).ToArray());

        var second = await harness.ClickAsync(Button(first, "下一页"));
        Assert.Contains("第 2/3 页", second.TgText());
        Assert.Contains("[alpha]\n11. task-11", second.TgText());
        Assert.Contains("[beta]\n13. task-01", second.TgText());
        CollectionAssert.AreEqual(new[] { "上一页", "刷新", "下一页" },
            second.TgButtonRows().Last().Buttons.Select(b => b.Text).ToArray());
        var detail = await harness.ClickAsync(Button(second, "13"));
        Assert.Contains("名称：beta:task-01", detail.TgText());
        var returned = await harness.ClickAsync(Button(detail, "返回列表"));
        Assert.Contains("第 2/3 页", returned.TgText());

        var last = await harness.ClickAsync(Button(returned, "下一页"));
        Assert.Contains("第 3/3 页", last.TgText());
        Assert.Contains("[beta]\n21. task-09", last.TgText());
        CollectionAssert.AreEqual(new[] { "21" }, last.TgButtonRows().First().Buttons.Select(b => b.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "上一页", "刷新" },
            last.TgButtonRows().Last().Buttons.Select(b => b.Text).ToArray());
        var refreshed = await harness.ClickAsync(Button(last, "刷新"));
        Assert.Contains("第 3/3 页", refreshed.TgText());
    }

    [TestMethod]
    public async Task SingleAndEmptyTaskPagesOnlyShowRefreshNavigation()
    {
        await using var harness = new Harness();
        var empty = await harness.Provider.BuildListAsync(Context());
        CollectionAssert.AreEqual(new[] { "刷新" }, empty.TgButtonTexts().ToArray());
        harness.Registry.RegisterPlugin("plugin", [new TestTask("plugin:only", _ => Task.CompletedTask)]);
        var single = await harness.Provider.BuildListAsync(Context());
        Assert.Contains("[plugin]\n1. only", single.TgText());
        CollectionAssert.AreEqual(new[] { "1", "刷新" }, single.TgButtonTexts().ToArray());
        var detail = await harness.ClickAsync(Button(single, "1"));
        Assert.Contains("名称：plugin:only", detail.TgText());
    }

    [TestMethod]
    public async Task ListAndErrorPagesStayBoundedAndNavigate()
    {
        await using var harness = new Harness();
        var tasks = Enumerable.Range(0, 12).Select(i => new TestTask($"task-{i:00}", _ => i == 0
            ? Task.FromException(new InvalidOperationException(new string('x', 6000))) : Task.CompletedTask)).ToArray();
        harness.Registry.RegisterPlugin("plugin", tasks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tasks[0].ExecuteAsync());
        var root = await harness.Provider.BuildListAsync(Context());
        Assert.Contains("第 1/2 页", root.TgText());
        var page2 = await harness.ClickAsync(root.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "下一页").Payload);
        Assert.Contains("第 2/2 页", page2.TgText());
        Assert.DoesNotContain("task-00", page2.TgText());
        var detail = await harness.ClickAsync(root.TgButtonRows().First().Buttons.First().Payload);
        var errors = await harness.ClickAsync(Button(detail, "查看报错详情"));
        Assert.IsLessThan(4096, errors.TgText().Length);
        var next = await harness.ClickAsync(Button(errors, "下一页"));
        Assert.Contains("第 2/", next.TgText());
        Assert.IsLessThan(4096, next.TgText().Length);
    }

    [TestMethod]
    public async Task ExecutionCancelAndEnableButtonsControlCurrentTaskAndScheduleSeparately()
    {
        await using var harness = new Harness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new TestTask("buttons", async token => { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        harness.Registry.RegisterPlugin("plugin", [task]);
        var root = await harness.Provider.BuildListAsync(Context());
        var detail = await harness.ClickAsync(root.TgButtonRows().First().Buttons.First().Payload);
        var running = await harness.ClickAsync(Button(detail, "执行一次"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(task.IsRunning);
        var disabled = await harness.ClickAsync(Button(running, "禁用调度"));
        Assert.IsFalse(task.Enabled);
        Assert.IsTrue(task.IsRunning);
        var canceling = await harness.ClickAsync(Button(disabled, "取消本次执行"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (task.IsRunning) await Task.Delay(5, timeout.Token);
        Assert.AreEqual(ManagedTaskOutcome.Canceled, task.Status.Outcome);
        Assert.IsFalse(task.Enabled);
        var enabled = await harness.ClickAsync(Button(canceling, "启用调度"));
        Assert.IsTrue(task.Enabled);
        Assert.IsFalse(task.IsRunning);
        Assert.Contains("已启用", enabled.TgText());
    }

    [TestMethod]
    public async Task StateWriteFailureIsVisibleOnTaskPanel()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var harness = new Harness(Store(path));
            harness.Registry.RegisterPlugin("plugin", [new TestTask("write-test", _ => Task.CompletedTask)]);
            var task = harness.Registry.Tasks.Single();
            task.Enabled = false;
            Assert.IsNotNull(task.StatePersistenceError);
            var list = await harness.Provider.BuildListAsync(Context());
            var details = await harness.ClickAsync(list.TgButtonRows().First().Buttons.First().Payload);
            Assert.Contains("状态保存失败", details.TgText());
        }
        finally { File.Delete(path); }
    }

    private static ManagedTaskStateStore Store(string path)
        => new(NullLogger<ManagedTaskStateStore>.Instance, new TestEnvironment(path));

    private static string Button(CommandResponse response, string text)
        => response.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == text).Payload;

    private static CommandContext Context()
        => new(new CommandRequest { Platform = BotPlatform.Telegram, UserId = "owner", ChatId = "chat", ChatType = BotChatType.Private, MessageId = "message", BotInstanceId = "bot" },
            new ResolvedIdentity(1, UserPrivilege.Owner, BotPlatform.Telegram, "owner"), TimeProvider.System.GetTimestamp(), CancellationToken.None);

    private sealed class TestTask(string name, Func<CancellationToken, Task> run) : ManagedTaskBase(true, "10 0 * * *", TimeProvider.System)
    {
        public override string Name => name;
        public override string Description => "Test task";
        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken) => run(cancellationToken);
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MutableIdentityCache : IIdentityCache
    {
        public UserPrivilege Privilege { get; set; } = UserPrivilege.Owner;
        public Task<CachedIdentity?> GetAsync(BotPlatform platform, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<CachedIdentity?>(new CachedIdentity(1, Privilege));
        public Task SetAsync(BotPlatform platform, string userId, CachedIdentity identity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(BotPlatform platform, string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly CoreDbContext _db;
        private readonly CallbackExecutionService _callbacks;
        public MutableIdentityCache Identity { get; } = new();
        public ManagedTaskRegistry Registry { get; }
        public TaskCommandDslProvider Provider { get; }

        public Harness(ManagedTaskStateStore? stateStore = null)
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            _services = services.BuildServiceProvider();
            var store = new CallbackActionStore(_services.GetRequiredService<IDistributedCache>(), Options.Create(new CallbackActionOptions()));
            Registry = new ManagedTaskRegistry([], stateStore);
            var control = new ManagedTaskControlService(Registry, NullLogger<ManagedTaskControlService>.Instance);
            Provider = new TaskCommandDslProvider(Registry, control, store, TimeProvider.System);
            _db = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            _callbacks = new CallbackExecutionService(new CoreIdentityService(_db, Identity, TimeProvider.System), store,
                _services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, taskProvider: Provider);
        }

        public Task<CommandResponse> ClickAsync(string payload, string chat = "chat")
            => _callbacks.ExecuteAsync(new CallbackRequest { Platform = BotPlatform.Telegram, UserId = "owner", ChatId = chat,
                ChatType = BotChatType.Private, BotInstanceId = "bot", MessageId = "panel-message", Payload = payload });

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _services.DisposeAsync();
        }
    }
}
