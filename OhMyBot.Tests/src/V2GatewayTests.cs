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
            "/p"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("ping", client.LastRequest.Command);
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
    public void TelegramUserInfoRendererShowsCoreIdOnlyWhenCoreReturnsIt()
    {
        var renderer = new UserInfoTelegramRenderer();
        var userResponse = CreateUserInfoResponse(includeCoreUserId: false);
        var adminResponse = CreateUserInfoResponse(includeCoreUserId: true);

        var userMessage = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(userResponse).Single()).Text;
        var adminMessage = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(adminResponse).Single()).Text;

        Assert.IsFalse(userMessage.Contains("ID:", StringComparison.Ordinal));
        Assert.Contains("ID: 42", adminMessage);
        Assert.Contains("用户信息", adminMessage);
        Assert.Contains("telegram:123456", adminMessage);
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
            DisplayName = "Tester",
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

        public Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CommandResponse());
        }

        public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateMixedRoutes());
        }
    }

    private sealed class FakeQQClient : QQGateway.ICommandRouterClient
    {
        public Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResponse());
        }

        public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateMixedRoutes());
        }
    }
}
