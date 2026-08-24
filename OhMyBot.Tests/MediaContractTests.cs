using Google.Protobuf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OhMyBot.Contracts.Grpc;
using OhMyBot.TelegramGateway;

namespace OhMyBot.Tests;

[TestClass]
public sealed class MediaContractTests
{
    [TestMethod]
    public async Task TelegramGatewayForwardsReplyMediaOnlyAsCommandMedia()
    {
        var routes = new GetRoutesResponse { Version = 1 };
        routes.Routes.Add(new RouteDescriptor
        {
            Command = "imgcvt",
            CoreCommand = "imgcvt",
            SupportPlatforms = 1,
            SupportChatTypes = 3,
            Enabled = true,
            AcceptsReplyMedia = true
        });
        var client = new FakeClient(routes);
        var gateway = new TelegramCommandGateway(client);
        await gateway.ReloadAsync("tg");

        var media = new CommandMedia
        {
            FileName = "source.jpg",
            ContentType = "image/jpeg",
            Content = ByteString.CopyFromUtf8("image")
        };
        await gateway.ExecuteAsync(
            new GatewayCommandRequest("chat", "user", "message", "/imgcvt png", ReplyMedia: media),
            "tg");

        Assert.IsNotNull(client.LastRequest);
        Assert.AreEqual("source.jpg", client.LastRequest.ReplyMedia.FileName);
        CollectionAssert.AreEqual(media.Content.ToByteArray(), client.LastRequest.ReplyMedia.Content.ToByteArray());
    }

    private sealed class FakeClient(GetRoutesResponse routes) : ICommandRouterClient
    {
        public CommandRequest? LastRequest { get; private set; }

        public Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CommandResponse { Telegram = new TelegramResponse() });
        }

        public Task<CommandResponse> ExecuteCallbackAsync(CallbackRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResponse { Telegram = new TelegramResponse() });

        public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(routes);

        public Task<UserProfileResponse> RecordUserProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserProfileResponse { Recorded = true });
    }
}
