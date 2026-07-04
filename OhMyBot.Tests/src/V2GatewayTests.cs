using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
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

        CollectionAssert.AreEqual(new[] { "第一条", "第二条" }, renderer.Render(response).ToArray());
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
    }
}
