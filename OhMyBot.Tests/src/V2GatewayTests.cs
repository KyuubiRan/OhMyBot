using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.QQGateway;
using OhMyBot.TelegramGateway;
using OhMyBot.TelegramGateway.Rendering;
using Telegram.Bot.Types.Enums;
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

        Assert.AreEqual(CommandResponseDataKind.Unspecified, ignored.DataKind);
        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("ping", client.LastRequest.Command);
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
        Assert.AreEqual(CommandResponseDataKind.Unspecified, response.DataKind);
        Assert.AreEqual(string.Empty, response.Message);
        Assert.IsNull(client.LastRequest);
    }

    [TestMethod]
    public void TelegramPingRendererUsesStructuredData()
    {
        var renderer = new PingTelegramRenderer();
        var response = new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.Ping,
            Ping = new PingData { ElapsedMs = 12 }
        };

        var message = renderer.Render(response).Single();

        var textMessage = Assert.IsInstanceOfType<TelegramTextMessage>(message);
        Assert.AreEqual(default, textMessage.ParseMode);
        Assert.AreEqual("Pong！Core：12ms", textMessage.Text);
    }

    [TestMethod]
    public void TelegramUserInfoRendererFormatsTelegramUserInfo()
    {
        var renderer = new UserInfoTelegramRenderer();
        var userResponse = CreateUserInfoResponse(includeCoreUserId: false);
        var adminResponse = CreateUserInfoResponse(includeCoreUserId: true);

        var userMessage = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(userResponse).Single()).Text;
        var adminTextMessage = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(adminResponse).Single());

        Assert.IsFalse(userMessage.Contains("ID: 42", StringComparison.Ordinal));
        Assert.IsFalse(adminTextMessage.Text.Contains("ID: 42", StringComparison.Ordinal));
        Assert.AreEqual(ParseMode.MarkdownV2, adminTextMessage.ParseMode);
        Assert.Contains("UID: `123456`", adminTextMessage.Text);
        Assert.Contains("用户名: `@tester`", adminTextMessage.Text);
        Assert.Contains("昵称: `User Test`", adminTextMessage.Text);
    }

    [TestMethod]
    public void TelegramUserInfoRendererFormatsVerifiedUserPrivilege()
    {
        var renderer = new UserInfoTelegramRenderer();
        var response = CreateUserInfoResponse(includeCoreUserId: false);
        response.UserInfo.Privilege = UserPrivilege.VerifiedUser;

        var message = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(response).Single()).Text;

        Assert.Contains("权限: `verified-user`", message);
    }

    [TestMethod]
    public void TelegramUserInfoRendererOmitsUsernameWhenMissing()
    {
        var renderer = new UserInfoTelegramRenderer();
        var response = CreateUserInfoResponse(includeCoreUserId: false);
        response.UserInfo.Identities.Single().Username = string.Empty;

        var message = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(response).Single()).Text;

        Assert.IsFalse(message.Contains("用户名:", StringComparison.Ordinal));
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

    private static CommandResponse CreateUserInfoResponse(bool includeCoreUserId)
    {
        var data = new UserInfoData
        {
            Privilege = UserPrivilege.Admin
        };
        data.Identities.Add(new PlatformIdentityData
        {
            Platform = BotPlatform.Telegram,
            Uid = "123456",
            DisplayName = "User Test",
            Username = "tester"
        });

        if (includeCoreUserId)
        {
            data.CoreUserId = 42;
        }

        return new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.UserInfo,
            UserInfo = data
        };
    }

    private sealed class FakeTelegramClient : ICommandRouterClient
    {
        public CommandRequest? LastRequest { get; private set; }

        public UserProfileRequest? LastProfileRequest { get; private set; }

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
            LastProfileRequest = request;
            return Task.FromResult(new UserProfileResponse { Recorded = true });
        }
    }

    private sealed class FakeQQClient : QQGateway.ICommandRouterClient
    {
        public CommandRequest? LastRequest { get; private set; }

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
