using Microsoft.Extensions.Options;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.OneBotV11.Events.Messages.Group;
using OhMyBot.OneBotV11.Events.Messages.Private;
using OhMyBot.QQGateway;
using OhMyBot.TelegramGateway;
using GatewayCommandRequest = OhMyBot.TelegramGateway.GatewayCommandRequest;
using ICommandRouterClient = OhMyBot.TelegramGateway.ICommandRouterClient;

namespace OhMyBot.Tests;

[TestClass]
public class V2GatewayTests
{
    [TestMethod]
    public async Task TelegramReloadCachesOnlyTelegramCommands()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);

        var routes = await gateway.ReloadAsync("tg");

        Assert.HasCount(1, routes);
        Assert.IsTrue(routes.Any(route => route.Command == "ping"));
        Assert.IsTrue(routes.Single(route => route.Command == "ping").Aliases.Contains("p"));
        Assert.HasCount(1, gateway.Routes);
    }

    [TestMethod]
    public async Task QQReloadCachesOnlyQQCommands()
    {
        var client = new FakeQQClient();
        var gateway = new QQCommandGateway(client);

        var routes = await gateway.ReloadAsync("qq");

        Assert.HasCount(1, routes);
        Assert.AreEqual("qqonly", routes[0].Command);
        Assert.HasCount(1, gateway.Routes);
    }

    [TestMethod]
    public async Task TelegramGatewayForwardsRouteCommand()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "/p",
            Username: "tester",
            ChatType: BotChatType.Group,
            FirstName: "Test",
            LastName: "User",
            ReplyToUserId: "reply-user"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("ping", client.LastRequest.Command);
        Assert.AreEqual(BotChatType.Group, client.LastRequest.ChatType);
        Assert.AreEqual("tester", client.LastRequest.Username);
        Assert.AreEqual("Test", client.LastRequest.FirstName);
        Assert.AreEqual("User", client.LastRequest.LastName);
        Assert.AreEqual("reply-user", client.LastRequest.ReplyToUserId);
    }

    [TestMethod]
    public async Task TelegramGatewayForwardsHierarchicalCommandArguments()
    {
        var routes = CreateMixedRoutes();
        routes.Routes.Add(new RouteDescriptor
        {
            Command = "ai",
            CoreCommand = "ai",
            Description = "AI.",
            Usage = "/ai <category> <command>",
            RequiredPrivilege = UserPrivilege.User,
            SupportPlatforms = 1,
            Enabled = true
        });
        var client = new FakeTelegramClient(routes);
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "/ai router bind username password"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("ai", client.LastRequest.Command);
        CollectionAssert.AreEqual(new[] { "router", "bind", "username", "password" }, client.LastRequest.Args.ToArray());
    }

    [TestMethod]
    public async Task TelegramGatewayUsesConfiguredCommandPrefixes()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(
            client,
            Options.Create(new TelegramGatewayOptions { CommandPrefixes = ["!"] }));
        await gateway.ReloadAsync("tg");

        var ignored = await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "/p"), "tg");
        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "!p"), "tg");

        Assert.HasCount(0, ignored.TgMessages());
        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("ping", client.LastRequest.Command);
    }

    [TestMethod]
    public async Task TelegramGatewayCanHandleKnownCommandsOnly()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        Assert.IsTrue(gateway.CanHandle("/ping"));
        Assert.IsTrue(gateway.CanHandle("/p"));
        Assert.IsTrue(gateway.CanHandle("/reload"));
        Assert.IsFalse(gateway.CanHandle("/unknown"));
        Assert.IsFalse(gateway.CanHandle("hello"));
    }

    [TestMethod]
    public async Task TelegramInfoUsesTextMentionUserIdAsArgument()
    {
        var client = new FakeTelegramClient(CreateTelegramInfoRoutes());
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "admin",
            "message",
            "/info @display",
            TextMentionUserId: "mentioned-user"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("info", client.LastRequest.Command);
        CollectionAssert.AreEqual(new[] { "mentioned-user" }, client.LastRequest.Args.ToArray());
    }

    [TestMethod]
    public async Task TelegramInfoKeepsUsernameMentionArgument()
    {
        var client = new FakeTelegramClient(CreateTelegramInfoRoutes());
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "admin",
            "message",
            "/info @target_user"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("info", client.LastRequest.Command);
        CollectionAssert.AreEqual(new[] { "@target_user" }, client.LastRequest.Args.ToArray());
    }

    [TestMethod]
    public async Task TelegramSetPrivilegeUsesTextMentionUserIdAsArgument()
    {
        var client = new FakeTelegramClient(CreateTelegramSetPrivilegeRoutes());
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "admin",
            "message",
            "/setpriv @display",
            TextMentionUserId: "mentioned-user"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("setpriv", client.LastRequest.Command);
        CollectionAssert.AreEqual(new[] { "mentioned-user" }, client.LastRequest.Args.ToArray());
    }

    [TestMethod]
    public async Task TelegramGatewayForwardsCallback()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);

        await gateway.ExecuteCallbackAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            BotInstanceId = "tg",
            ChatId = "chat",
            UserId = "user",
            MessageId = "message",
            Payload = "hash"
        });

        Assert.IsNotNull(client.LastCallbackRequest);
        Assert.AreEqual("hash", client.LastCallbackRequest.Payload);
        Assert.AreEqual("user", client.LastCallbackRequest.UserId);
    }

    [TestMethod]
    public async Task QQGatewayUsesDefaultCommandPrefixes()
    {
        var client = new FakeQQClient();
        var gateway = new QQCommandGateway(client);
        await gateway.ReloadAsync("qq");

        await gateway.ExecuteAsync(new OhMyBot.QQGateway.GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "!qqonly"), "qq");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("qqonly", client.LastRequest.Command);
    }

    [TestMethod]
    public async Task TelegramGatewayRecordsUserProfile()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);

        var recorded = await gateway.RecordUserProfileAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "hello",
            Username: "tester",
            ChatType: BotChatType.Private,
            FirstName: "Test",
            LastName: "User"), "tg");

        Assert.IsTrue(recorded);
        Assert.IsNotNull(client.LastProfileRequest);
        Assert.AreEqual(BotPlatform.Telegram, client.LastProfileRequest.Platform);
        Assert.AreEqual("user", client.LastProfileRequest.Uid);
        Assert.AreEqual("tester", client.LastProfileRequest.Username);
        Assert.AreEqual("Test", client.LastProfileRequest.FirstName);
        Assert.AreEqual("User", client.LastProfileRequest.LastName);
    }

    [TestMethod]
    public async Task TelegramGatewayIgnoresUnknownCommand()
    {
        var client = new FakeTelegramClient();
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        var response = await gateway.ExecuteAsync(new GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "/unknown"), "tg");

        Assert.AreEqual(0, response.Code);
        Assert.HasCount(0, response.TgMessages());
        Assert.IsNull(client.LastRequest);
    }

    [TestMethod]
    public void QQResponseRendererReturnsMessageTexts()
    {
        var renderer = new QQResponseRenderer();
        var response = new CommandResponse
        {
            Qq = new QqResponse
            {
                Messages =
                {
                    new QqMessage { Text = "第一条" },
                    new QqMessage { Text = "第二条" }
                }
            }
        };

        CollectionAssert.AreEqual(
            new[] { "第一条", "第二条" },
            renderer.Render(response).Select(message => message.Text).ToArray());
    }

    private static GetRoutesResponse CreateMixedRoutes()
    {
        var response = new GetRoutesResponse { Version = 1 };
        response.Routes.Add(new RouteDescriptor
        {
            Command = "ping",
            CoreCommand = "ping",
            Description = "Ping.",
            Usage = "/ping",
            RequiredPrivilege = UserPrivilege.User,
            SupportPlatforms = 1,
            Enabled = true
        });
        response.Routes[0].Aliases.Add("p");
        response.Routes.Add(new RouteDescriptor
        {
            Command = "qqonly",
            CoreCommand = "qqonly",
            Description = "QQ only.",
            Usage = "/qqonly",
            RequiredPrivilege = UserPrivilege.User,
            SupportPlatforms = 2,
            Enabled = true
        });
        return response;
    }

    private static GetRoutesResponse CreateTelegramInfoRoutes()
    {
        var response = new GetRoutesResponse { Version = 1 };
        response.Routes.Add(new RouteDescriptor
        {
            Command = "info",
            CoreCommand = "info",
            Description = "Info.",
            Usage = "/info [uid]",
            RequiredPrivilege = UserPrivilege.User,
            SupportPlatforms = 1,
            Enabled = true
        });
        return response;
    }

    private static GetRoutesResponse CreateTelegramSetPrivilegeRoutes()
    {
        var response = new GetRoutesResponse { Version = 1 };
        response.Routes.Add(new RouteDescriptor
        {
            Command = "setpriv",
            CoreCommand = "setpriv",
            Description = "Set privilege.",
            Usage = "/setpriv [uid]",
            RequiredPrivilege = UserPrivilege.Admin,
            SupportPlatforms = 1,
            Enabled = true
        });
        return response;
    }

    private sealed class FakeTelegramClient : ICommandRouterClient
    {
        private readonly GetRoutesResponse _routes;

        public FakeTelegramClient(GetRoutesResponse? routes = null)
        {
            _routes = routes ?? CreateMixedRoutes();
        }

        public CommandRequest? LastRequest { get; private set; }

        public CallbackRequest? LastCallbackRequest { get; private set; }

        public UserProfileRequest? LastProfileRequest { get; private set; }

        public Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CommandResponse());
        }

        public Task<CommandResponse> ExecuteCallbackAsync(CallbackRequest request, CancellationToken cancellationToken = default)
        {
            LastCallbackRequest = request;
            return Task.FromResult(new CommandResponse());
        }

        public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_routes);
        }

        public Task<UserProfileResponse> RecordUserProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default)
        {
            LastProfileRequest = request;
            return Task.FromResult(new UserProfileResponse { Recorded = true });
        }
    }

    private sealed class FakeQQClient : QQGateway.ICommandRouterClient
    {
        public CommandRequest? LastRequest { get; private set; }

        public Task<CommandResponse> ExecuteCallbackAsync(CallbackRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResponse());
        }

        public Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CommandResponse());
        }

        public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateMixedRoutes());
        }

        public Task<UserProfileResponse> RecordUserProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new UserProfileResponse { Recorded = true });
        }

        public Task<BindQqMenuResponse> BindQqMenuAsync(BindQqMenuRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BindQqMenuResponse { Bound = true });
        }

        public Task<CommandResponse> ExecuteQqMenuSelectionAsync(QqMenuSelectionRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResponse());
        }

        public Task<PlatformRequestAck> ReportPlatformRequestAsync(PlatformRequestReport request, CancellationToken cancellationToken = default)
        {
            LastPlatformRequest = request;
            return Task.FromResult(new PlatformRequestAck { Accepted = true });
        }

        public PlatformRequestReport? LastPlatformRequest { get; private set; }
    }

    // QQ 待审批请求的翻译：flag 决定审批哪一条、kind 决定用哪个 OneBot 动作、group_id 决定是哪个群。
    // 这三项任何一项错位，审批就会落到别的请求上，而且用户侧完全看不出来。
    [TestMethod]
    public async Task QQRequestEventHandlerTranslatesOneBotRequestEvents()
    {
        var friend = await ReportAsync(new FriendAddRequestEvent
        {
            Flag = "flag-friend",
            RequesterId = 20001,
            Comment = "求加"
        });
        Assert.AreEqual(
            (PlatformRequestKind.FriendAdd, "flag-friend", "20001", string.Empty),
            (friend.Kind, friend.Flag, friend.RequesterId, friend.GroupId));

        var invite = await ReportAsync(new GroupJoinRequestEvent
        {
            SubType = "invite",
            Flag = "flag-invite",
            RequesterId = 20002,
            GroupId = 30001
        });
        Assert.AreEqual(
            (PlatformRequestKind.GroupInvite, "flag-invite", "20002", "30001"),
            (invite.Kind, invite.Flag, invite.RequesterId, invite.GroupId));

        var add = await ReportAsync(new GroupJoinRequestEvent
        {
            SubType = "add",
            Flag = "flag-add",
            RequesterId = 20003,
            GroupId = 30002
        });
        Assert.AreEqual(
            (PlatformRequestKind.GroupAdd, "flag-add", "20003", "30002"),
            (add.Kind, add.Flag, add.RequesterId, add.GroupId));
    }

    // QQ 的 request 事件只带 user_id。不补档案的话，owner 收到的通知里只有一串数字，
    // 根本没法判断该不该同意——所以档案补齐是这条通知有没有用的关键。
    [TestMethod]
    public async Task QQRequestEventHandlerFillsRequesterProfile()
    {
        var report = await ReportAsync(new FriendAddRequestEvent { Flag = "flag-friend", RequesterId = 20001 });

        Assert.AreEqual("申请人", report.RequesterName);
        Assert.AreEqual("申请人", report.RequesterProfile[PlatformRequestProfileKeys.Nickname]);
        Assert.AreEqual("male", report.RequesterProfile[PlatformRequestProfileKeys.Gender]);
        Assert.AreEqual("18", report.RequesterProfile[PlatformRequestProfileKeys.Age]);
        Assert.AreEqual("32", report.RequesterProfile[PlatformRequestProfileKeys.Level]);
        StringAssert.Contains(report.RequesterProfile[PlatformRequestProfileKeys.AvatarUrl], "20001");
    }

    // 消息等非 request 事件不该走上报通道：群聊每条消息都会流经同一个 OnEvent。
    [TestMethod]
    public async Task QQRequestEventHandlerIgnoresNonRequestEvents()
    {
        var (client, handler) = CreateRequestHandler();
        handler.Handle(new GroupMessageEvent { GroupId = 30001 });

        await Task.Delay(100);
        Assert.IsNull(client.LastPlatformRequest);
    }

    private static async Task<PlatformRequestReport> ReportAsync(OhMyBot.OneBotV11.Events.EventBase evt)
    {
        var (client, handler) = CreateRequestHandler();
        handler.Handle(evt);

        // Handle 把上报卸载到后台任务，避免阻塞 OneBot 收包循环。
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (client.LastPlatformRequest is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var report = client.LastPlatformRequest;
        Assert.IsNotNull(report, "请求事件应当被上报给 Core。");
        Assert.AreEqual(BotPlatform.Qq, report.Platform);
        Assert.AreEqual("qq-test", report.BotInstanceId);
        return report;
    }

    private static (FakeQQClient Client, QQRequestEventHandler Handler) CreateRequestHandler()
    {
        var client = new FakeQQClient();
        var handler = new QQRequestEventHandler(
            new QQCommandGateway(client),
            new FakeOneBotClient(),
            Options.Create(new QQGatewayOptions { BotInstanceId = "qq-test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QQRequestEventHandler>.Instance);
        return (client, handler);
    }

    // 只实现 get_stranger_info：请求事件上报路径只会用到它。
    private sealed class FakeOneBotClient : OhMyBot.OneBotV11.IOneBotClient
    {
        public OhMyBot.OneBotV11.Transport.OneBotTransportType TransportType
            => OhMyBot.OneBotV11.Transport.OneBotTransportType.WebSocket;

        public OhMyBot.OneBotV11.Transport.OneBotConnectionState ConnectionState
            => OhMyBot.OneBotV11.Transport.OneBotConnectionState.Connected;

        public event Func<OhMyBot.OneBotV11.Transport.OneBotConnectionState, ValueTask>? ConnectionStateChanged;
        public event Action<OhMyBot.OneBotV11.Events.EventBase>? OnEvent;
        public event Action<Exception>? OnException;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<OhMyBot.OneBotV11.Transport.OneBotActionResponse<System.Text.Json.JsonElement>> SendActionAsync(
            OhMyBot.OneBotV11.Transport.OneBotActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = ConnectionStateChanged;
            _ = OnEvent;
            _ = OnException;
            using var document = System.Text.Json.JsonDocument.Parse(
                """{"nickname":"申请人","sex":"male","age":18,"qqLevel":32}""");
            return Task.FromResult(new OhMyBot.OneBotV11.Transport.OneBotActionResponse<System.Text.Json.JsonElement>
            {
                Status = "ok",
                RetCode = 0,
                Data = document.RootElement.Clone()
            });
        }
    }

    // 网关侧档案去重的行为约束：未变则省下 gRPC，改名/超时则重新记录。QQ 与 TG 的 RecordedProfileCache
    // 为逐字节相同的副本，测其一即代表两者。
    [TestMethod]
    public void RecordedProfileCacheSkipsUnchangedWithinTtlAndRefreshesOnChangeOrExpiry()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new OhMyBot.QQGateway.RecordedProfileCache(clock, TimeSpan.FromMinutes(30));

        // 首次：应记录。
        Assert.IsTrue(cache.ShouldRecord("100", "alice"));
        cache.MarkRecorded("100", "alice");

        // 相同 uid + 相同签名、TTL 内：跳过（这正是省下的那次 gRPC）。
        Assert.IsFalse(cache.ShouldRecord("100", "alice"));

        // 档案变化（改名/改群名片）：立即失效并重新记录。
        Assert.IsTrue(cache.ShouldRecord("100", "alice-renamed"));
        cache.MarkRecorded("100", "alice-renamed");
        Assert.IsFalse(cache.ShouldRecord("100", "alice-renamed"));

        // 不同用户：各自独立。
        Assert.IsTrue(cache.ShouldRecord("200", "bob"));

        // TTL 到期后：即便未变也放行一次（顺带刷新，兜底 Core 侧记录被清）。
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.IsTrue(cache.ShouldRecord("100", "alice-renamed"));
    }

    [TestMethod]
    public void RecordedProfileCacheOnlyCommitsAfterMarkRecorded()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new OhMyBot.QQGateway.RecordedProfileCache(clock, TimeSpan.FromMinutes(30));

        // 只 ShouldRecord 不 MarkRecorded（模拟 gRPC 失败）：下次仍应记录，不会因为查过一次就被跳过。
        Assert.IsTrue(cache.ShouldRecord("100", "alice"));
        Assert.IsTrue(cache.ShouldRecord("100", "alice"));
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
