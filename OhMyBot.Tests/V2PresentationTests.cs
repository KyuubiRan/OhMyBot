using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Tests;

/// <summary>
/// 面板构建机制层（Pagination / PanelBuilder / TextLayout）的边界行为。
/// 这些工具被四个签到插件共用，一处回归会同时打断四个插件的面板，所以边界必须锁死。
/// </summary>
[TestClass]
public class V2PresentationTests
{
    [TestMethod]
    public void TotalPagesReturnsOneForEmptySoPageLabelStaysValid()
    {
        // 空集合返回 0 页的话，插件拼出来的会是「第 1/0 页」。
        Assert.AreEqual(1, Pagination.TotalPages(0, 8));
        Assert.AreEqual(1, Pagination.TotalPages(8, 8));
        Assert.AreEqual(2, Pagination.TotalPages(9, 8));
    }

    [TestMethod]
    public void NormalizePageClampsOutOfRangeInsteadOfThrowing()
    {
        // 页码来自用户手上的回调 payload，可能是账号被删之前存下的旧值——必须夹住而不是抛。
        Assert.AreEqual(0, Pagination.NormalizePage(-1, 9, 8));
        Assert.AreEqual(1, Pagination.NormalizePage(99, 9, 8));
        Assert.AreEqual(0, Pagination.NormalizePage(3, 0, 8));
    }

    [TestMethod]
    public async Task AddGridWrapsAtColumnCountAndKeepsRemainder()
    {
        var panel = CreatePanel();
        var response = CommandResponses.Text("panel", CreateContext());

        await panel.AddGridAsync(
            response,
            new[] { "a", "b", "c" },
            columns: 2,
            "test-action",
            item => item,
            item => new TestCallbackData(item));

        Assert.HasCount(2, response.TgButtonRows());
        Assert.HasCount(2, response.TgButtonRows()[0].Buttons);
        Assert.HasCount(1, response.TgButtonRows()[1].Buttons);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, response.TgButtonTexts().ToArray());
    }

    [TestMethod]
    public async Task AddPagerSkipsEntireRowForSinglePage()
    {
        // 单页时不能留一行空的翻页按钮——四家插件的「单页文案完全一致」依赖这个。
        var panel = CreatePanel();
        var response = CommandResponses.Text("panel", CreateContext());

        await panel.AddPagerAsync(
            response, "test-pager", page: 0, totalPages: 1,
            target => new TestCallbackData(target.ToString()), "上一页", "下一页");

        Assert.IsEmpty(response.TgButtonRows());
    }

    [TestMethod]
    public async Task AddPagerOmitsPreviousOnFirstPageAndNextOnLastPage()
    {
        var context = CreateContext();
        var first = CommandResponses.Text("panel", context);
        await CreatePanel().AddPagerAsync(
            first, "test-pager", page: 0, totalPages: 3,
            target => new TestCallbackData(target.ToString()), "上一页", "下一页");
        CollectionAssert.AreEqual(new[] { "下一页" }, first.TgButtonTexts().ToArray());

        var middle = CommandResponses.Text("panel", context);
        await CreatePanel().AddPagerAsync(
            middle, "test-pager", page: 1, totalPages: 3,
            target => new TestCallbackData(target.ToString()), "上一页", "下一页");
        CollectionAssert.AreEqual(new[] { "上一页", "下一页" }, middle.TgButtonTexts().ToArray());

        var last = CommandResponses.Text("panel", context);
        await CreatePanel().AddPagerAsync(
            last, "test-pager", page: 2, totalPages: 3,
            target => new TestCallbackData(target.ToString()), "上一页", "下一页");
        CollectionAssert.AreEqual(new[] { "上一页" }, last.TgButtonTexts().ToArray());
    }

    [TestMethod]
    public async Task ButtonPayloadRoundTripsThroughObjectTypedData()
    {
        // PanelBuilder.ButtonAsync 把 data 声明成 object，PutAsync<T> 因此 T = object。
        // System.Text.Json 对根级 object 按运行时类型序列化——若哪天退化成静态类型，payload 会变成 {}，
        // 表现为所有按钮点下去「按钮数据无效」。这条锁死它。
        var cache = new FakeDistributedCache();
        var store = new CallbackActionStore(cache, Options.Create(new CallbackActionOptions()));
        var panel = new PanelBuilder(store, CreateContext());

        var button = await panel.ButtonAsync("test-action", "文字", new TestCallbackData("payload-value"));
        var action = await store.GetAsync(button.Payload);

        Assert.IsNotNull(action);
        StringAssert.Contains(action.DataJson, "payload-value");
    }

    [TestMethod]
    public void JoinOrEmptyFallsBackToEmptyTextOnlyWhenNoValues()
    {
        Assert.AreEqual("无", TextLayout.JoinOrEmpty([], "、", "无"));
        Assert.AreEqual("签到、浏览", TextLayout.JoinOrEmpty(["签到", "浏览"], "、", "无"));
    }

    [TestMethod]
    public void JoinLinesSkipsNullSoConditionalRowsCollapse()
    {
        // 米游社只有国服账号才有社区任务行，用 null 表示「这行不存在」。
        Assert.AreEqual("a\nc", TextLayout.JoinLines("a", null, "c"));
    }

    [TestMethod]
    public void AsTelegramEditIfSpecifiedLeavesReplyTargetIntactWhenBlank()
    {
        // AsTelegramEdit 会无条件清掉 ReplyToMessageId，这是回调路径需要的。
        // 但非编辑路径（命令首次响应）必须保住 ReplyToMessageId，否则回复不再挂在用户消息下面。
        foreach (var blank in new string?[] { null, string.Empty, "   " })
        {
            var response = CommandResponses.Text("panel", CreateContext());
            var before = response.TgSingle().ReplyToMessageId;

            response.AsTelegramEditIfSpecified(blank);

            Assert.AreEqual(before, response.TgSingle().ReplyToMessageId);
            Assert.AreEqual(string.Empty, response.TgSingle().EditMessageId);
        }
    }

    [TestMethod]
    public void AsTelegramEditIfSpecifiedStillClearsReplyTargetWhenEditing()
    {
        var response = CommandResponses.Text("panel", CreateContext());

        response.AsTelegramEditIfSpecified("42");

        Assert.AreEqual("42", response.TgSingle().EditMessageId);
        Assert.AreEqual(string.Empty, response.TgSingle().ReplyToMessageId);
    }

    private static PanelBuilder CreatePanel()
    {
        var store = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        return new PanelBuilder(store, CreateContext());
    }

    private static CommandContext CreateContext()
    {
        return new CommandContext(
            new CommandRequest
            {
                Platform = BotPlatform.Telegram,
                BotInstanceId = "tg",
                ChatId = "chat",
                UserId = "user",
                MessageId = "message",
                ChatType = BotChatType.Private
            },
            new ResolvedIdentity(1, UserPrivilege.VerifiedUser, BotPlatform.Telegram, "tg"),
            TimeProvider.System.GetTimestamp(),
            CancellationToken.None);
    }

    private sealed record TestCallbackData(string Value);

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            _items.TryGetValue(key, out var value);
            return value;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _items[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _items.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
