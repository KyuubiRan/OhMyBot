using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Commanding.Qq;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Tests;

[TestClass]
public sealed class V2QqMenuTests
{
    [TestMethod]
    public async Task ConverterRendersTelegramButtonsAsNumberedMenuAndStoresPayloads()
    {
        var store = NewStore();
        var converter = new QqMenuConverter(store);
        var response = new CommandResponse
        {
            Telegram = new TelegramResponse
            {
                Messages =
                {
                    new TelegramMessage
                    {
                        Text = "请选择要签到的账号",
                        ParseMode = TelegramParseMode.None,
                        ButtonRows =
                        {
                            new ResponseButtonRow { Buttons = { new ResponseButton { Text = "账号A", Payload = "p1" } } },
                            new ResponseButtonRow
                            {
                                Buttons =
                                {
                                    new ResponseButton { Text = "账号B", Payload = "p2" },
                                    new ResponseButton { Text = "全部", Payload = "p3" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var result = await converter.ToQqAsync(response, BotChatType.Private);

        // Telegram 分支被换成 QQ 分支。
        Assert.AreEqual(CommandResponse.PlatformResponseOneofCase.Qq, result.PlatformResponseCase);
        var message = result.Qq.Messages.Single();
        StringAssert.Contains(message.Text, "请选择要签到的账号");
        StringAssert.Contains(message.Text, "1. 账号A");
        StringAssert.Contains(message.Text, "2. 账号B");
        StringAssert.Contains(message.Text, "3. 全部");
        Assert.IsFalse(string.IsNullOrEmpty(message.MenuToken));

        // 编号必须映射回按钮 payload：绑定到某条消息后按序号解析。
        await store.BindAsync("chat", "m1", "u1", BotChatType.Private, message.MenuToken);
        Assert.AreEqual("p2", await store.ResolveAsync("chat", "m1", "u1", BotChatType.Private, 1));
    }

    [TestMethod]
    public async Task ConverterKeepsPlainTextWithoutMenuTokenWhenNoButtons()
    {
        var converter = new QqMenuConverter(NewStore());
        var response = new CommandResponse
        {
            Telegram = new TelegramResponse
            {
                Messages = { new TelegramMessage { Text = "签到成功", ParseMode = TelegramParseMode.None } }
            }
        };

        var result = await converter.ToQqAsync(response, BotChatType.Private);

        var message = result.Qq.Messages.Single();
        Assert.AreEqual("签到成功", message.Text);
        Assert.AreEqual(string.Empty, message.MenuToken);
    }

    [TestMethod]
    public async Task ConverterRecoversPlainTextFromMarkdownV2Escaping()
    {
        var converter = new QqMenuConverter(NewStore());
        // 模拟 notify 面板的 TelegramMarkdown 文本（含转义与 code span）。
        var markdown = MarkdownV2.Escape("[消息订阅管理]") + "\n当前已启用：" + MarkdownV2.CodeSpan("AI Router 自动签到");
        var response = new CommandResponse
        {
            Telegram = new TelegramResponse
            {
                Messages =
                {
                    new TelegramMessage
                    {
                        Text = markdown,
                        ParseMode = TelegramParseMode.MarkdownV2,
                        ButtonRows = { new ResponseButtonRow { Buttons = { new ResponseButton { Text = "订阅", Payload = "p" } } } }
                    }
                }
            }
        };

        var result = await converter.ToQqAsync(response, BotChatType.Private);

        var text = result.Qq.Messages.Single().Text;
        StringAssert.Contains(text, "[消息订阅管理]");
        StringAssert.Contains(text, "当前已启用：AI Router 自动签到");
        Assert.IsFalse(text.Contains('\\'), "转义反斜杠应被还原");
        Assert.IsFalse(text.Contains('`'), "code span 反引号应被移除");
    }

    [TestMethod]
    public async Task ResolveScopesContextByRepliedMessageEvenWhenNewerMenuExists()
    {
        // 用户场景：菜单1 与 菜单2 先后发出（私聊同一用户）。回复菜单1 仍应解析菜单1 的选项。
        var store = NewStore();
        var t1 = await store.PutTokenAsync(["a1", "a2"]);
        var t2 = await store.PutTokenAsync(["b1", "b2"]);
        await store.BindAsync("chat", "menu1", "u1", BotChatType.Private, t1);
        await store.BindAsync("chat", "menu2", "u1", BotChatType.Private, t2);

        // 回复菜单1 → 菜单1 的选项（哪怕菜单2 更新）。
        Assert.AreEqual("a1", await store.ResolveAsync("chat", "menu1", "u1", BotChatType.Private, 0));
        // 私聊裸数字（无回复）→ 最近一次菜单（菜单2）。
        Assert.AreEqual("b1", await store.ResolveAsync("chat", null, "u1", BotChatType.Private, 0));
    }

    [TestMethod]
    public async Task ResolveRequiresReplyInGroupsAndRejectsOutOfRange()
    {
        var store = NewStore();
        var token = await store.PutTokenAsync(["x1", "x2"]);
        await store.BindAsync("group", "gm1", "u1", BotChatType.Group, token);

        // 群聊必须回复某条菜单：有 replyTo 命中。
        Assert.AreEqual("x2", await store.ResolveAsync("group", "gm1", "u1", BotChatType.Group, 1));
        // 群聊裸数字（无 replyTo、无 latest 指针）→ null。
        Assert.IsNull(await store.ResolveAsync("group", null, "u1", BotChatType.Group, 0));
        // 越界序号 → null。
        Assert.IsNull(await store.ResolveAsync("group", "gm1", "u1", BotChatType.Group, 5));
        // 未知消息 id → null。
        Assert.IsNull(await store.ResolveAsync("group", "unknown", "u1", BotChatType.Group, 0));
    }

    [TestMethod]
    public void ToPlainReversesMarkdownV2EscapingAndStripsCodeMarks()
    {
        Assert.AreEqual("[a] (b) .!-", MarkdownV2.ToPlain(MarkdownV2.Escape("[a] (b) .!-")));
        Assert.AreEqual("code", MarkdownV2.ToPlain(MarkdownV2.CodeSpan("code")));
        // 纯文本（无转义）保持不变。
        Assert.AreEqual("hello 世界", MarkdownV2.ToPlain("hello 世界"));
    }

    [TestMethod]
    public void AddButtonRowUpgradesQqTextResponseToTelegramShapeForConversion()
    {
        // ai-router 面板 builder 对 QQ 会先经 CommandResponses.Text 产出 QQ 纯文本分支，
        // 再 AddButtonRow —— 这里验证按钮扩展会就地把它迁移成带按钮的 Telegram 形态（否则会空引用）。
        var identity = new ResolvedIdentity(1, UserPrivilege.User, BotPlatform.Qq, "u1");
        var response = CommandResponses.Text("请选择账号", identity);
        Assert.AreEqual(CommandResponse.PlatformResponseOneofCase.Qq, response.PlatformResponseCase);

        response.AddButtonRow(new ResponseButtonRow { Buttons = { new ResponseButton { Text = "账号A", Payload = "p1" } } });

        Assert.AreEqual(CommandResponse.PlatformResponseOneofCase.Telegram, response.PlatformResponseCase);
        Assert.AreEqual("请选择账号", response.Telegram.Messages[0].Text);
        Assert.AreEqual("p1", response.Telegram.Messages[0].ButtonRows[0].Buttons[0].Payload);
    }

    [TestMethod]
    public async Task ConverterSurfacesSuccessToastIntoQqTextButSkipsErrorToast()
    {
        var converter = new QqMenuConverter(NewStore());

        // 成功响应（Code==0）：toast 并入正文。
        var success = new CommandResponse
        {
            Code = 0,
            CallbackAnswerText = "请至少勾选一个游戏",
            Telegram = new TelegramResponse
            {
                Messages =
                {
                    new TelegramMessage
                    {
                        Text = "游戏签到面板",
                        ButtonRows = { new ResponseButtonRow { Buttons = { new ResponseButton { Text = "开始签到", Payload = "p" } } } }
                    }
                }
            }
        };
        var successResult = await converter.ToQqAsync(success, BotChatType.Private);
        StringAssert.Contains(successResult.Qq.Messages[0].Text, "请至少勾选一个游戏");
        StringAssert.Contains(successResult.Qq.Messages[0].Text, "游戏签到面板");

        // 错误响应（Code!=0）：toast 已在正文，跳过以免重复。
        var error = new CommandResponse
        {
            Code = 1,
            CallbackAnswerText = "这个按钮只能由原发起用户操作。",
            Telegram = new TelegramResponse
            {
                Messages = { new TelegramMessage { Text = "错误：这个按钮只能由原发起用户操作。" } }
            }
        };
        var errorResult = await converter.ToQqAsync(error, BotChatType.Private);
        Assert.AreEqual("错误：这个按钮只能由原发起用户操作。", errorResult.Qq.Messages[0].Text);
    }

    private static QqMenuStore NewStore()
    {
        return new QqMenuStore(new FakeDistributedCache(), Options.Create(new QqMenuOptions()));
    }

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
