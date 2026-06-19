using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Tests;

[TestClass]
public class V2GatewayTests
{
    [TestMethod]
    public async Task TelegramReloadCachesOnlyTelegramCommands()
    {
        var client = new FakeTelegramClient();
        var gateway = new OhMyBot.TelegramGateway.TelegramCommandGateway(client);

        var routes = await gateway.ReloadAsync("tg");

        Assert.HasCount(2, routes);
        Assert.IsTrue(routes.Any(route => route.Command == "ping"));
        Assert.IsTrue(routes.Any(route => route.Command == "p"));
        Assert.HasCount(2, gateway.Routes);
    }

    [TestMethod]
    public async Task QQReloadCachesOnlyQQCommands()
    {
        var client = new FakeQQClient();
        var gateway = new OhMyBot.QQGateway.QQCommandGateway(client);

        var routes = await gateway.ReloadAsync("qq");

        Assert.HasCount(1, routes);
        Assert.AreEqual("qqonly", routes[0].Command);
        Assert.HasCount(1, gateway.Routes);
    }

    [TestMethod]
    public async Task TelegramGatewayForwardsRouteCommand()
    {
        var client = new FakeTelegramClient();
        var gateway = new OhMyBot.TelegramGateway.TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        await gateway.ExecuteAsync(new OhMyBot.TelegramGateway.GatewayCommandRequest(
            "chat",
            "user",
            "message",
            "/p"), "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("p", client.LastRequest.Command);
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
        response.Routes.Add(new RouteDescriptor
        {
            Command = "p",
            CoreCommand = "ping",
            Description = "Ping alias.",
            Usage = "/p",
            RequiredPrivilege = UserPrivilege.User,
            SupportPlatforms = 1,
            Enabled = true
        });
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

    private sealed class FakeTelegramClient : OhMyBot.TelegramGateway.ICommandRouterClient
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

    private sealed class FakeQQClient : OhMyBot.QQGateway.ICommandRouterClient
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
