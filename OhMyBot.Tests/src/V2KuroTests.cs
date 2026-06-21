using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Kuro;
using OhMyBot.Core.Notifications;
using OhMyBot.TelegramGateway.Rendering;
using Telegram.Bot.Types.Enums;

namespace OhMyBot.Tests;

[TestClass]
public class V2KuroTests
{
    [TestMethod]
    public async Task KuroHttpClientUsesRequestScopedTokenHeaders()
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.kurobbs.com")
        };
        var client = new KuroHttpClient(httpClient, Options.Create(new KuroOptions
        {
            DevCode = "dev-a",
            DistinctId = "distinct-a",
            Version = "3.0.4"
        }));

        await client.GetMineAsync("token-a");
        await client.GetMineAsync(new KuroRequestCredential("token-b", "dev-b", "distinct-b"));

        Assert.HasCount(2, handler.Requests);
        Assert.IsFalse(httpClient.DefaultRequestHeaders.Contains("token"));
        Assert.AreEqual("token-a", handler.Requests[0].Headers.GetValues("token").Single());
        Assert.AreEqual("token-b", handler.Requests[1].Headers.GetValues("token").Single());
        Assert.AreEqual("dev-a", handler.Requests[0].Headers.GetValues("devCode").Single());
        Assert.AreEqual("distinct-a", handler.Requests[0].Headers.GetValues("distinct_id").Single());
        Assert.AreEqual("dev-b", handler.Requests[1].Headers.GetValues("devCode").Single());
        Assert.AreEqual("distinct-b", handler.Requests[1].Headers.GetValues("distinct_id").Single());
        Assert.AreEqual("h5", handler.Requests[0].Headers.GetValues("source").Single());
    }

    [TestMethod]
    public async Task KuroAccountsAllowMultipleAccountsPerUserAndModelHasUniqueBbsUserId()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new CoreUser { Id = 1 });
        dbContext.CoreUsers.Add(new CoreUser { Id = 2 });
        dbContext.KuroAccounts.Add(new KuroAccount
        {
            CoreUserId = 1,
            BbsUserId = 1001,
            DisplayName = "A",
            TokenCiphertext = "token-a"
        });
        dbContext.KuroAccounts.Add(new KuroAccount
        {
            CoreUserId = 1,
            BbsUserId = 1002,
            DisplayName = "B",
            TokenCiphertext = "token-b"
        });
        await dbContext.SaveChangesAsync();

        Assert.AreEqual(2, await dbContext.KuroAccounts.CountAsync(account => account.CoreUserId == 1));

        var entityType = dbContext.Model.FindEntityType(typeof(KuroAccount));
        Assert.IsNotNull(entityType);
        var bbsUserIdIndex = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(KuroAccount.BbsUserId)]));
        Assert.IsTrue(bbsUserIdIndex.IsUnique);
    }

    [TestMethod]
    public void TelegramKuroRendererFormatsStructuredBindResult()
    {
        var renderer = new KuroTelegramRenderer();
        var response = new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.KuroBindResult,
            KuroBindResult = new KuroBindResultData
            {
                UpdatedExisting = false,
                Account = new KuroAccountItem
                {
                    Id = 10,
                    BbsUserId = 1001,
                    DisplayName = "库洛_账号",
                    AutoSignEnabled = true
                }
            }
        };
        response.KuroBindResult.Account.Roles.Add(new KuroGameRoleItem
        {
            GameId = 3,
            GameName = "鸣潮",
            RoleId = 2001,
            RoleName = "漂泊者",
            GameLevel = "77"
        });

        var message = Assert.IsInstanceOfType<TelegramTextMessage>(renderer.Render(response).Single());

        Assert.AreEqual(ParseMode.MarkdownV2, message.ParseMode);
        Assert.Contains("库街区账号绑定成功", message.Text);
        Assert.Contains("库洛_账号", message.Text);
        Assert.Contains("鸣潮", message.Text);
    }

    [TestMethod]
    public async Task KuroAutoSignPanelUsesPagedAccountFirstLevel()
    {
        await using var dbContext = CreateDbContext();
        var builder = new KuroResponseBuilder(
            new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions())),
            new NotificationSubscriptionService(dbContext, TimeProvider.System),
            TimeProvider.System);
        var accounts = Enumerable.Range(1, 9)
            .Select(index => new KuroAccount
            {
                Id = index,
                CoreUserId = 1,
                BbsUserId = 1000 + index,
                DisplayName = $"Kuro{index}",
                AutoSignEnabled = index % 2 == 0
            })
            .ToArray();

        var response = await builder.BuildAutoSignPanelAsync(CreateContext(), accounts);

        Assert.Contains("第 1/2 页", response.Message);
        Assert.IsTrue(response.ButtonRows.SelectMany(row => row.Buttons).Any(button => button.Text == "下一页"));
        Assert.IsFalse(response.ButtonRows.SelectMany(row => row.Buttons).Any(button => button.Text.Contains("Kuro9", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task KuroAutoSignGamePanelAddsPagedRolesAndBackButton()
    {
        await using var dbContext = CreateDbContext();
        var builder = new KuroResponseBuilder(
            new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions())),
            new NotificationSubscriptionService(dbContext, TimeProvider.System),
            TimeProvider.System);
        var account = new KuroAccount
        {
            Id = 10,
            CoreUserId = 1,
            BbsUserId = 1001,
            DisplayName = "Kuro"
        };
        for (var index = 1; index <= 7; index++)
        {
            account.Roles.Add(new KuroGameRole
            {
                Id = index,
                KuroAccountId = account.Id,
                GameId = 3,
                GameName = "鸣潮",
                RoleId = 2000 + index,
                RoleName = $"角色{index}"
            });
        }

        var response = await builder.BuildAutoSignGamePanelAsync(CreateContext(), [account], account.Id);
        var buttons = response.ButtonRows.SelectMany(row => row.Buttons).Select(button => button.Text).ToArray();

        Assert.Contains("第 1/2 页", response.Message);
        CollectionAssert.Contains(buttons, "下一页");
        CollectionAssert.Contains(buttons, "开启/关闭全部");
        CollectionAssert.Contains(buttons, "返回");
    }

    private static OhMyBotV2DbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OhMyBotV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OhMyBotV2DbContext(options);
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":200,"msg":"ok","success":true,"data":{"mine":{"userId":"1001","userName":"tester"}}}""")
            });
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            _items.TryGetValue(key, out var value);
            return value;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _items[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _items.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
