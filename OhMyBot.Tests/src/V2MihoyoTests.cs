using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Integrations.Mihoyo;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Security;

namespace OhMyBot.Tests;

[TestClass]
public class V2MihoyoTests
{
    [TestMethod]
    public void MihoyoDsBuildDsMatchesKnownVector()
    {
        var ds = MihoyoDs.BuildDs("d9200c846b10886e8c874fc33c8f308b", 1700000000, "abcdef");
        Assert.AreEqual("1700000000,abcdef,9b2c35c8f05bc0d2f56c66ed85fd8743", ds);
    }

    [TestMethod]
    public void MihoyoDsBuildDs2MatchesKnownVector()
    {
        var ds = MihoyoDs.BuildDs2("t0qEgfub6cvueAPgR5m9aQWWVciEer7v", 1700000000, 150000, string.Empty, "{\"gids\":\"2\"}");
        Assert.AreEqual("1700000000,150000,ef37242c9324233e54d22323256f1381", ds);
    }

    [TestMethod]
    public void MihoyoDsGetDeviceIdIsDeterministicUuid3()
    {
        Assert.AreEqual("75928c39-60c0-3f76-841c-d39978344ad4", MihoyoDs.GetDeviceId("12345"));
        Assert.AreEqual(MihoyoDs.GetDeviceId("12345"), MihoyoDs.GetDeviceId("12345"));
    }

    [TestMethod]
    public void SetCookieTokenReplacesOrAppends()
    {
        Assert.AreEqual("a=1;cookie_token=new;b=2", MihoyoAccountService.SetCookieToken("a=1;cookie_token=old;b=2", "new"));
        Assert.AreEqual("a=1;b=2;cookie_token=new", MihoyoAccountService.SetCookieToken("a=1;b=2", "new"));
    }

    [TestMethod]
    public void BuildStokenCookieAddsMidForV2Stoken()
    {
        Assert.AreEqual("stuid=42;stoken=abc", MihoyoAccountService.BuildStokenCookie(42, "abc", "m1"));
        Assert.AreEqual("stuid=42;stoken=v2_xyz;mid=m1", MihoyoAccountService.BuildStokenCookie(42, "v2_xyz", "m1"));
    }

    [TestMethod]
    public void GameCatalogParsesAliasesAndFiltersByRegion()
    {
        Assert.IsTrue(MihoyoGameCatalog.TryParse("原神", out var genshin));
        Assert.AreEqual("genshin", genshin.Key);
        Assert.IsTrue(MihoyoGameCatalog.TryParse("zzz", out var zzz));
        Assert.AreEqual("nap_cn", zzz.CnGameBiz);
        Assert.IsFalse(MihoyoGameCatalog.TryParse("unknown", out _));

        // 崩坏2 仅国服支持
        Assert.IsTrue(MihoyoGameCatalog.ForRegion(MihoyoRegion.Cn).Any(game => game.Key == "honkai2"));
        Assert.IsFalse(MihoyoGameCatalog.ForRegion(MihoyoRegion.Os).Any(game => game.Key == "honkai2"));
    }

    [TestMethod]
    public async Task BindOsCookieSeedsRoles()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new StubHandler());
        var result = await service.BindAsync(1, "ltuid_v2=98765; ltoken_v2=v2_abc; account_mid_v2=mid9", MihoyoRegion.Os);

        Assert.IsFalse(result.UpdatedExisting);
        Assert.AreEqual(MihoyoRegion.Os, result.Account.Region);
        Assert.AreEqual(98765, result.Account.Stuid);
        Assert.AreEqual(string.Empty, result.Account.StokenCiphertext);
        // 国际服支持 5 个游戏
        Assert.HasCount(5, result.Account.Roles);
        Assert.IsTrue(result.Account.Roles.All(role => role.GameUid == 0 && role.Region == "os"));
    }

    [TestMethod]
    public async Task BindCnCookieWithStokenRefreshesCookieToken()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new StubHandler());
        var result = await service.BindAsync(1, "account_id=1122; stuid=1122; stoken=v2_tok; mid=m7; cookie_token=stale", MihoyoRegion.Cn);

        Assert.AreEqual(MihoyoRegion.Cn, result.Account.Region);
        Assert.AreEqual(1122, result.Account.Stuid);
        Assert.AreNotEqual(string.Empty, result.Account.StokenCiphertext);
        // cookie_token 被刷新写入
        StringAssert.Contains(result.Account.CookieCiphertext, "cookie_token=fresh-token");
    }

    [TestMethod]
    public async Task BindCnCookieWithLtokenButNoStokenBindsAsCn()
    {
        // 用户真实场景：国服 Cookie 含 ltoken/cookie_token 但无 stoken
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new StubHandler());
        var result = await service.BindAsync(
            1,
            "account_mid_v2=0pq_mhy; account_id_v2=4984975; ltuid_v2=4984975; cookie_token=Eix; account_id=4984975; ltoken=dkXU; ltuid=4984975",
            MihoyoRegion.Cn);

        Assert.AreEqual(MihoyoRegion.Cn, result.Account.Region);
        Assert.AreEqual(4984975, result.Account.Stuid);
        // 无 stoken → 不存 stoken 密文
        Assert.AreEqual(string.Empty, result.Account.StokenCiphertext);
    }

    [TestMethod]
    public async Task BindCnCookieWithoutCookieTokenOrStokenThrows()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new StubHandler());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.BindAsync(1, "account_id=1; ltoken=x", MihoyoRegion.Cn));
    }

    [TestMethod]
    public async Task BindCookieWithoutStuidThrows()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new StubHandler());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.BindAsync(1, "some=value; another=thing", MihoyoRegion.Cn));
    }

    [TestMethod]
    public async Task BuildNotifyAccountPanelReflectsSubscriptionState()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new CoreUser { Id = 1, Privilege = UserPrivilege.VerifiedUser });
        dbContext.MihoyoAccounts.Add(new MihoyoAccount
        {
            Id = 10,
            CoreUserId = 1,
            Region = MihoyoRegion.Cn,
            Stuid = 555,
            DisplayName = "tester",
            CookieCiphertext = "c"
        });
        await dbContext.SaveChangesAsync();

        var subscriptionService = new NotificationSubscriptionService(dbContext, TimeProvider.System);
        await subscriptionService.EnableAsync(1, BotPlatform.Telegram, "tg", "chat", NotificationTypes.MihoyoAutoSign, 10);

        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var builder = new MihoyoResponseBuilder(callbackStore, subscriptionService, TimeProvider.System);
        var accounts = await new MihoyoAccountService(dbContext, CreateClient(new StubHandler()), new PlainSecretProtector(), TimeProvider.System)
            .ListByOwnerAsync(1, noTracking: true);

        var response = await builder.BuildNotifyAccountPanelAsync(CreateContext(), accounts);

        // 意图：账号出现在面板中，且其订阅开关按钮反映“已启用”状态
        Assert.AreEqual("[开] tester", response.TgButtonRows()[0].Buttons[0].Text);
        StringAssert.Contains(response.TgText(), "tester");
    }

    private static MihoyoHttpClient CreateClient(HttpMessageHandler handler)
    {
        return new MihoyoHttpClient(new HttpClient(handler), Options.Create(new MihoyoOptions()));
    }

    [TestMethod]
    public async Task BuildGameSignPanelRendersCheckboxPerOwnedGameAndActionRow()
    {
        var builder = CreateBuilder();
        var account = new MihoyoAccount
        {
            Id = 7,
            CoreUserId = 1,
            Region = MihoyoRegion.Cn,
            DisplayName = "tester",
            Roles =
            {
                new MihoyoGameRole { GameBiz = "hk4e_cn", GameName = "原神", Nickname = "阿光", GameUid = 188888888, Level = "60" },
                new MihoyoGameRole { GameBiz = "hkrpg_cn", GameName = "崩坏：星穹铁道" }
            }
        };

        // 仅勾选原神：星穹铁道应显示为未勾选
        var response = await builder.BuildGameSignPanelAsync(CreateContext(), account, ["genshin"]);

        // 意图：每个已同步游戏一个开关，√/× 反映勾选状态，底部提供「签到」「返回」
        Assert.HasCount(3, response.TgButtonRows());
        Assert.AreEqual("[√] 原神", response.TgButtonRows()[0].Buttons[0].Text);
        Assert.AreEqual("[×] 崩坏：星穹铁道", response.TgButtonRows()[1].Buttons[0].Text);
        Assert.AreEqual("签到", response.TgButtonRows()[2].Buttons[0].Text);
        Assert.AreEqual("返回", response.TgButtonRows()[2].Buttons[1].Text);
        // 意图：面板正文展示各游戏角色的昵称/UID/等级，便于确认签到对象（MarkdownV2 转义括号与点号）
        StringAssert.Contains(response.TgText(), "阿光\\(188888888\\) Lv\\.60");
    }

    [TestMethod]
    public async Task BuildGameSignPanelPromptsInitWhenNoRoles()
    {
        var builder = CreateBuilder();
        var account = new MihoyoAccount { Id = 9, DisplayName = "empty", Region = MihoyoRegion.Cn };

        var response = await builder.BuildGameSignPanelAsync(CreateContext(), account, []);

        Assert.IsEmpty(response.TgButtonRows());
        StringAssert.Contains(response.TgText(), "game init");
    }

    [TestMethod]
    public async Task BuildGameSignSelectionAddsSignAllOnlyForMultipleAccounts()
    {
        var builder = CreateBuilder();
        var many = new[]
        {
            new MihoyoAccount { Id = 1, DisplayName = "a", Region = MihoyoRegion.Cn },
            new MihoyoAccount { Id = 2, DisplayName = "b", Region = MihoyoRegion.Os }
        };

        var multi = await builder.BuildGameSignSelectionAsync(CreateContext(), many);
        // 两个账号 + 「全部签到」
        Assert.HasCount(3, multi.TgButtonRows());
        Assert.AreEqual("全部签到", multi.TgButtonRows()[^1].Buttons[0].Text);

        var single = await builder.BuildGameSignSelectionAsync(CreateContext(), [many[0]]);
        // 单账号无需「全部签到」
        Assert.HasCount(1, single.TgButtonRows());
    }

    [TestMethod]
    public void AvailableGameKeysReturnsOwnedKeysInCatalogOrder()
    {
        var account = new MihoyoAccount
        {
            Roles =
            {
                new MihoyoGameRole { GameBiz = "hkrpg_cn" },
                new MihoyoGameRole { GameBiz = "hk4e_cn" }
            }
        };

        // 目录顺序：genshin 先于 sr，且与角色录入顺序无关
        CollectionAssert.AreEqual(new[] { "genshin", "sr" }, MihoyoResponseBuilder.AvailableGameKeys(account).ToArray());
    }

    [TestMethod]
    public void ResolveGameSignSelectionUsesStoredSelectionOrDefaultsToAll()
    {
        var account = new MihoyoAccount
        {
            Roles =
            {
                new MihoyoGameRole { GameBiz = "hk4e_cn" },
                new MihoyoGameRole { GameBiz = "hkrpg_cn" }
            }
        };

        // 未设置（空串）→ 默认全选
        CollectionAssert.AreEqual(new[] { "genshin", "sr" }, MihoyoResponseBuilder.ResolveGameSignSelection(account).ToArray());

        // 记住上次仅勾选星铁 → 面板沿用该选择
        account.GameSignSelection = "sr";
        CollectionAssert.AreEqual(new[] { "sr" }, MihoyoResponseBuilder.ResolveGameSignSelection(account).ToArray());

        // 显式清空标记 → 无勾选（区别于“未设置”的默认全选）
        account.GameSignSelection = MihoyoResponseBuilder.NoneSelectionSentinel;
        Assert.IsEmpty(MihoyoResponseBuilder.ResolveGameSignSelection(account));

        // 存储项在当前可用游戏中已全部失效 → 视为无勾选
        account.GameSignSelection = "zzz";
        Assert.IsEmpty(MihoyoResponseBuilder.ResolveGameSignSelection(account));
    }

    [TestMethod]
    public void SerializeGameSignSelectionUsesSentinelForEmpty()
    {
        Assert.AreEqual(MihoyoResponseBuilder.NoneSelectionSentinel, MihoyoResponseBuilder.SerializeGameSignSelection([]));
        Assert.AreEqual("genshin,sr", MihoyoResponseBuilder.SerializeGameSignSelection(["genshin", "sr"]));
    }

    private static MihoyoResponseBuilder CreateBuilder()
    {
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        return new MihoyoResponseBuilder(callbackStore, new NotificationSubscriptionService(CreateDbContext(), TimeProvider.System), TimeProvider.System);
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

    private static MihoyoAccountService CreateAccountService(OhMyBotV2DbContext dbContext, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var client = new MihoyoHttpClient(httpClient, Options.Create(new MihoyoOptions()));
        return new MihoyoAccountService(dbContext, client, new PlainSecretProtector(), TimeProvider.System);
    }

    private static OhMyBotV2DbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OhMyBotV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OhMyBotV2DbContext(options);
    }

    [TestMethod]
    public async Task BindCnCookieParsesRolesWithNumericLevel()
    {
        // 真实接口里 level 是 JSON 数字、game_uid 是字符串
        await using var dbContext = CreateDbContext();
        var service = CreateAccountService(dbContext, new RolesStubHandler());
        var result = await service.BindAsync(
            1,
            "account_id=4984975; cookie_token=Eix; ltoken=dkXU; ltuid=4984975",
            MihoyoRegion.Cn);

        Assert.AreEqual(MihoyoRegion.Cn, result.Account.Region);
        // 账号名应为米游社昵称，而非游戏角色昵称
        Assert.AreEqual("米游社小明", result.Account.DisplayName);
        var role = result.Account.Roles.First(r => r.GameUid == 188888888);
        Assert.AreEqual("旅行者阿光", role.Nickname);
        Assert.AreEqual("60", role.Level);
        Assert.AreEqual("cn_gf01", role.Region);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var json = url.Contains("getCookieAccountInfoBySToken")
                ? """{"retcode":0,"message":"OK","data":{"cookie_token":"fresh-token"}}"""
                : url.Contains("getUserGameRolesByCookie")
                    ? """{"retcode":0,"message":"OK","data":{"list":[]}}"""
                    : """{"retcode":0,"message":"OK","data":{}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class RolesStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            // level 是数字，game_uid 是字符串；账号昵称来自 getUserFullInfo
            var json = url.Contains("getUserFullInfo")
                ? """{"retcode":0,"message":"OK","data":{"user_info":{"uid":"4984975","nickname":"米游社小明"}}}"""
                : url.Contains("getUserGameRolesByCookie")
                    ? """{"retcode":0,"message":"OK","data":{"list":[{"game_biz":"hk4e_cn","region":"cn_gf01","game_uid":"188888888","nickname":"旅行者阿光","level":60,"region_name":"天空岛"}]}}"""
                    : """{"retcode":0,"message":"OK","data":{}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class FakeDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => _items.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options) => _items[key] = value;

        public Task SetAsync(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options, CancellationToken token = default)
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
