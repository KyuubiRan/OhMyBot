using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Admin;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Linking;
using OhMyBot.Core.Routing;

namespace OhMyBot.Tests;

[TestClass]
public class V2CoreTests
{
    [TestMethod]
    public async Task FirstPlatformMessageCreatesCoreUserAndIdentity()
    {
        await using var dbContext = CreateDbContext();
        var identityService = new CoreIdentityService(dbContext, new FakeIdentityCache(), TimeProvider.System);

        var identity = await identityService.EnsureIdentityAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "10001",
            DisplayName = "Tester",
            Username = "tester"
        });

        Assert.AreNotEqual(0, identity.CoreUserId);
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        Assert.AreEqual(1, await dbContext.PlatformIdentities.CountAsync());
        Assert.AreEqual("Tester", identity.DisplayName);
    }

    [TestMethod]
    public async Task LinkWithoutArgsWritesTokenWithFiveMinuteTtl()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "link"));

        Assert.IsNull(response.Error);
        Assert.AreEqual(TimeSpan.FromMinutes(5), tokenStore.LastTtl);
        Assert.HasCount(1, tokenStore.Tokens);
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        Assert.IsNull(dbContext.Model.FindEntityType("LinkToken"));
    }

    [TestMethod]
    public async Task LinkWithTokenMergesIdentityAndDeletesToken()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link"));
        var token = tokenStore.LastToken!;

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-current", "link", token));

        Assert.IsNull(response.Error);
        Assert.IsFalse(tokenStore.Tokens.ContainsKey(token));
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        Assert.AreEqual(2, await dbContext.PlatformIdentities.CountAsync());
    }

    [TestMethod]
    public async Task MissingOrConsumedTokenReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-current", "link", "missing"));

        Assert.IsNotNull(response.Error);
        Assert.AreEqual("LinkTokenInvalid", response.Error.Code);
    }

    [TestMethod]
    public async Task MergingExistingUsersKeepsHighestPrivilege()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, tokenStore, identityCache: identityCache);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-admin", "ping"));

        var qqIdentity = await dbContext.PlatformIdentities
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Platform == BotPlatform.Qq);
        qqIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link"));
        var token = tokenStore.LastToken!;

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-admin", "link", token));

        Assert.IsNull(response.Error);
        var mergedUser = await dbContext.CoreUsers.SingleAsync();
        Assert.AreEqual(UserPrivilege.Admin, mergedUser.Privilege);

        var cachedTelegramIdentity = await identityCache.GetAsync(BotPlatform.Telegram, "tg-owner");
        var cachedQqIdentity = await identityCache.GetAsync(BotPlatform.Qq, "qq-admin");
        Assert.IsNotNull(cachedTelegramIdentity);
        Assert.IsNotNull(cachedQqIdentity);
        Assert.AreEqual(mergedUser.Id, cachedTelegramIdentity.CoreUserId);
        Assert.AreEqual(mergedUser.Id, cachedQqIdentity.CoreUserId);
        Assert.AreEqual(UserPrivilege.Admin, cachedTelegramIdentity.Privilege);
        Assert.AreEqual(UserPrivilege.Admin, cachedQqIdentity.Privilege);
    }

    [TestMethod]
    public async Task MergingExistingUsersKeepsSmallestCoreUserIdRegardlessOfLinkDirection()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-old", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-new", "ping"));

        var firstUser = await dbContext.PlatformIdentities
            .Include(identity => identity.CoreUser)
            .Where(identity => identity.Platform == BotPlatform.Telegram)
            .Select(identity => identity.CoreUser)
            .SingleAsync();
        var secondUser = await dbContext.PlatformIdentities
            .Include(identity => identity.CoreUser)
            .Where(identity => identity.Platform == BotPlatform.Qq)
            .Select(identity => identity.CoreUser)
            .SingleAsync();

        firstUser.CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        secondUser.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await dbContext.SaveChangesAsync();

        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-new", "link"));
        var token = tokenStore.LastToken!;
        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-old", "link", token));

        Assert.IsNull(response.Error);
        var retainedUser = await dbContext.CoreUsers.SingleAsync();
        Assert.AreEqual(Math.Min(firstUser.Id, secondUser.Id), retainedUser.Id);
        Assert.AreEqual(2, await dbContext.PlatformIdentities.CountAsync(identity => identity.CoreUserId == retainedUser.Id));
    }

    [TestMethod]
    public void CommandListIsFilteredBySupportedPlatforms()
    {
        var registry = new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        registry.Add(new CommandRegistration(
            "qqonly",
            "QQ only command.",
            "/qqonly",
            UserPrivilege.User,
            SupportedPlatforms.QQ,
            _ => Task.FromResult(CommandResponses.Text("ok"))));

        var routeStore = CreateRouteStore(registry);
        routeStore.InitializeAsync().GetAwaiter().GetResult();

        var telegramRoutes = routeStore.GetRoutes(BotPlatform.Telegram);
        var qqRoutes = routeStore.GetRoutes(BotPlatform.Qq);

        Assert.IsFalse(telegramRoutes.Any(route => route.Command == "qqonly"));
        Assert.IsTrue(qqRoutes.Any(route => route.Command == "qqonly"));
    }

    [TestMethod]
    public async Task InsufficientPrivilegeReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var registry = new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        registry.Add(new CommandRegistration(
            "owner",
            "Owner command.",
            "/owner",
            UserPrivilege.Owner,
            SupportedPlatforms.All,
            _ => Task.FromResult(CommandResponses.Text("secret"))));

        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "owner"));

        Assert.IsNotNull(response.Error);
        Assert.AreEqual("PrivilegeDenied", response.Error.Code);
    }

    [TestMethod]
    public async Task CachedIdentityCanAuthorizeWithoutDatabaseIdentityLookup()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        await identityCache.SetAsync(
            BotPlatform.Telegram,
            "tg-owner",
            new CachedIdentity(999, UserPrivilege.Owner));

        var registry = new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        registry.Add(new CommandRegistration(
            "owner",
            "Owner command.",
            "/owner",
            UserPrivilege.Owner,
            SupportedPlatforms.All,
            _ => Task.FromResult(CommandResponses.Text("secret"))));

        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "owner",
                    CoreCommand = "owner",
                    Description = "Owner command.",
                    Usage = "/owner",
                    RequiredPrivilege = "Owner",
                    SupportPlatforms = ["Telegram"],
                    Enabled = true
                }
            ]
        };

        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry, routeDocument, identityCache);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "owner"));

        Assert.IsNull(response.Error);
        Assert.AreEqual("secret", response.Messages.Single().Text);
        Assert.AreEqual(0, await dbContext.PlatformIdentities.CountAsync());
    }

    [TestMethod]
    public async Task AliasRouteExecutesCoreCommand()
    {
        await using var dbContext = CreateDbContext();
        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "p",
                    CoreCommand = "ping",
                    Description = "Ping alias.",
                    Usage = "/p",
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    Enabled = true
                }
            ]
        };
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), routeDocument: routeDocument);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "p"));

        Assert.IsNull(response.Error);
        Assert.IsTrue(response.Messages.Single().Text.StartsWith("Pong"));
    }

    [TestMethod]
    public async Task RouteTargetMissingReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "broken",
                    CoreCommand = "missing",
                    Description = "Broken route.",
                    Usage = "/broken",
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    Enabled = true
                }
            ]
        };
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), routeDocument: routeDocument);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "broken"));

        Assert.IsNotNull(response.Error);
        Assert.AreEqual("RouteTargetMissing", response.Error.Code);
    }

    [TestMethod]
    public async Task RouteCannotLowerCorePrivilege()
    {
        await using var dbContext = CreateDbContext();
        var registry = new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        registry.Add(new CommandRegistration(
            "owner",
            "Owner command.",
            "/owner",
            UserPrivilege.Owner,
            SupportedPlatforms.All,
            _ => Task.FromResult(CommandResponses.Text("secret"))));

        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "owner",
                    CoreCommand = "owner",
                    Description = "Owner command.",
                    Usage = "/owner",
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    Enabled = true
                }
            ]
        };
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry, routeDocument);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "owner"));

        Assert.IsNotNull(response.Error);
        Assert.AreEqual("PrivilegeDenied", response.Error.Code);
    }

    [TestMethod]
    public async Task InvalidRouteJsonKeepsPreviousSnapshot()
    {
        var registry = new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        var routeStore = CreateRouteStore(registry);
        await routeStore.InitializeAsync();
        var before = routeStore.GetRoutes(BotPlatform.Telegram);

        await File.WriteAllTextAsync(routeStore.RouteFilePath, "{ invalid json");
        var reloaded = await routeStore.ReloadAsync(writeMergedFile: true);
        var after = routeStore.GetRoutes(BotPlatform.Telegram);

        Assert.IsFalse(reloaded);
        Assert.HasCount(before.Count, after);
        Assert.IsTrue(after.Any(route => route.Command == "ping"));
    }

    [TestMethod]
    public async Task AdminUserPrivilegeCommandCreatesIdentityAndRefreshesCache()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var executor = new AdminCommandExecutor(dbContext, identityCache, TimeProvider.System);

        var result = await executor.ExecuteAsync("user -p telegram -uid 123456 -sp owner");

        Assert.IsTrue(result.Success);
        var identity = await dbContext.PlatformIdentities
            .Include(item => item.CoreUser)
            .SingleAsync(item => item.Platform == BotPlatform.Telegram && item.PlatformUserId == "123456");
        Assert.AreEqual(UserPrivilege.Owner, identity.CoreUser.Privilege);

        var cached = await identityCache.GetAsync(BotPlatform.Telegram, "123456");
        Assert.IsNotNull(cached);
        Assert.AreEqual(identity.CoreUserId, cached.CoreUserId);
        Assert.AreEqual(UserPrivilege.Owner, cached.Privilege);
    }

    [TestMethod]
    public async Task AdminUserPrivilegeCommandUpdatesExistingIdentity()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var identityService = new CoreIdentityService(dbContext, identityCache, TimeProvider.System);
        var identity = await identityService.EnsureIdentityAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "123456"
        });
        var executor = new AdminCommandExecutor(dbContext, identityCache, TimeProvider.System);

        var result = await executor.ExecuteAsync("user -p telegram -uid 123456 -sp admin");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        var user = await dbContext.CoreUsers.SingleAsync();
        Assert.AreEqual(identity.CoreUserId, user.Id);
        Assert.AreEqual(UserPrivilege.Admin, user.Privilege);

        var cached = await identityCache.GetAsync(BotPlatform.Telegram, "123456");
        Assert.IsNotNull(cached);
        Assert.AreEqual(UserPrivilege.Admin, cached.Privilege);
    }

    [TestMethod]
    public async Task AdminUserQueryReadsDatabaseIdentity()
    {
        await using var dbContext = CreateDbContext();
        var executor = new AdminCommandExecutor(dbContext, new FakeIdentityCache(), TimeProvider.System);
        await executor.ExecuteAsync("user -p qq -uid 998877 -sp admin");

        var result = await executor.ExecuteAsync("user -p qq -uid 998877");

        Assert.IsTrue(result.Success);
        Assert.Contains("CoreUserId:", result.Message);
        Assert.Contains("Privilege: admin", result.Message);
        Assert.Contains("qq:998877", result.Message);
    }

    [TestMethod]
    public async Task AdminUserCommandCanQueryAndSetByCoreUserId()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var executor = new AdminCommandExecutor(dbContext, identityCache, TimeProvider.System);
        await executor.ExecuteAsync("user --platform=telegram --uid=123456 --set-priv=admin");
        await executor.ExecuteAsync("user -p qq -uid 998877 -sp user");
        var user = await dbContext.CoreUsers.SingleAsync(user => user.Privilege == UserPrivilege.Admin);

        var queryResult = await executor.ExecuteAsync($"user -id {user.Id} -gp");
        var setResult = await executor.ExecuteAsync($"user --id={user.Id} -sp owner");

        Assert.IsTrue(queryResult.Success);
        Assert.Contains($"CoreUserId: {user.Id}", queryResult.Message);
        Assert.IsTrue(setResult.Success);
        Assert.AreEqual(UserPrivilege.Owner, (await dbContext.CoreUsers.FindAsync(user.Id))!.Privilege);

        var cached = await identityCache.GetAsync(BotPlatform.Telegram, "123456");
        Assert.IsNotNull(cached);
        Assert.AreEqual(user.Id, cached.CoreUserId);
        Assert.AreEqual(UserPrivilege.Owner, cached.Privilege);
    }

    [TestMethod]
    public async Task AdminHelpShowsStructuredUserUsage()
    {
        await using var dbContext = CreateDbContext();
        var executor = new AdminCommandExecutor(dbContext, new FakeIdentityCache(), TimeProvider.System);

        var help = await executor.ExecuteAsync("help");
        var userHelp = await executor.ExecuteAsync("help user");

        Assert.IsTrue(help.Success);
        Assert.Contains("Available commands:", help.Message);
        Assert.Contains("user", help.Message);
        Assert.IsTrue(userHelp.Success);
        Assert.Contains("usage: user", userHelp.Message);
        Assert.Contains("Manage core/platform users.", userHelp.Message);
        Assert.Contains("-id, --id", userHelp.Message);
        Assert.Contains("-sp, --set-priv", userHelp.Message);
        Assert.Contains("examples:", userHelp.Message);
    }

    [TestMethod]
    public async Task AdminUserCommandReturnsErrorsForInvalidInput()
    {
        await using var dbContext = CreateDbContext();
        var executor = new AdminCommandExecutor(dbContext, new FakeIdentityCache(), TimeProvider.System);

        var missingUid = await executor.ExecuteAsync("user -p telegram");
        var invalidPlatform = await executor.ExecuteAsync("user -p discord -uid 1");
        var invalidPrivilege = await executor.ExecuteAsync("user -p telegram -uid 1 -sp root");
        var mutuallyExclusive = await executor.ExecuteAsync("user -id 1 -p telegram -uid 1");
        var unknownOption = await executor.ExecuteAsync("user --missing value");

        Assert.IsFalse(missingUid.Success);
        Assert.Contains("Options '-p' and '-uid' must be specified together.", missingUid.Message);
        Assert.Contains("usage: user", missingUid.Message);
        Assert.IsFalse(invalidPlatform.Success);
        Assert.Contains("Invalid platform. Supported values: telegram, qq.", invalidPlatform.Message);
        Assert.IsFalse(invalidPrivilege.Success);
        Assert.Contains("Invalid privilege. Supported values: user, admin, owner.", invalidPrivilege.Message);
        Assert.IsFalse(mutuallyExclusive.Success);
        Assert.Contains("Options '-id' and '-p/-uid' are mutually exclusive.", mutuallyExclusive.Message);
        Assert.IsFalse(unknownOption.Success);
        Assert.Contains("Unknown option '--missing'.", unknownOption.Message);
    }

    [TestMethod]
    public void AdminCommandParserSupportsQuotedArguments()
    {
        var tokens = AdminCommandParser.Tokenize("user -p telegram -uid \"123 456\" -sp owner");

        CollectionAssert.AreEqual(
            new[] { "user", "-p", "telegram", "-uid", "123 456", "-sp", "owner" },
            tokens.ToArray());
    }

    private static OhMyBotV2DbContext CreateDbContext()
    {
        return new OhMyBotV2DbContext(new DbContextOptionsBuilder<OhMyBotV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static CommandExecutionService CreateCommandService(
        OhMyBotV2DbContext dbContext,
        FakeLinkTokenStore tokenStore,
        CommandRegistry? registry = null,
        RouteDocument? routeDocument = null,
        FakeIdentityCache? identityCache = null)
    {
        registry ??= new CommandRegistry(CommandExecutionService.CreateBuiltInCommands());
        identityCache ??= new FakeIdentityCache();
        var routeStore = CreateRouteStore(registry, routeDocument);
        routeStore.InitializeAsync().GetAwaiter().GetResult();

        return new CommandExecutionService(
            dbContext,
            new CoreIdentityService(dbContext, identityCache, TimeProvider.System),
            registry,
            routeStore,
            tokenStore,
            Options.Create(new LinkTokenOptions()),
            TimeProvider.System);
    }

    private static RouteStore CreateRouteStore(CommandRegistry registry, RouteDocument? routeDocument = null)
    {
        var routeFilePath = Path.Combine(Path.GetTempPath(), "ohmybot-tests", Guid.NewGuid().ToString("N"), "route.json");
        if (routeDocument is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(routeFilePath)!);
            File.WriteAllText(routeFilePath, JsonSerializer.Serialize(routeDocument, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        return new RouteStore(
            registry,
            Options.Create(new RouteOptions { Path = routeFilePath }),
            NullLogger<RouteStore>.Instance);
    }

    private static CommandRequest CreateRequest(BotPlatform platform, string userId, string command, params string[] args)
    {
        return new CommandRequest
        {
            Platform = platform,
            BotInstanceId = "test",
            ChatId = "chat",
            UserId = userId,
            MessageId = Guid.NewGuid().ToString("N"),
            Command = command,
            Args = { args }
        };
    }

    private sealed class FakeLinkTokenStore : ILinkTokenStore
    {
        public Dictionary<string, LinkTokenPayload> Tokens { get; } = new(StringComparer.Ordinal);

        public string? LastToken { get; private set; }

        public TimeSpan? LastTtl { get; private set; }

        public Task SetAsync(string token, LinkTokenPayload payload, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            Tokens[token] = payload;
            LastToken = token;
            LastTtl = ttl;
            return Task.CompletedTask;
        }

        public Task<LinkTokenPayload?> GetAsync(string token, CancellationToken cancellationToken = default)
        {
            Tokens.TryGetValue(token, out var payload);
            return Task.FromResult(payload);
        }

        public Task RemoveAsync(string token, CancellationToken cancellationToken = default)
        {
            Tokens.Remove(token);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdentityCache : IIdentityCache
    {
        private readonly Dictionary<string, CachedIdentity> _identities = new(StringComparer.Ordinal);

        public Task<CachedIdentity?> GetAsync(
            BotPlatform platform,
            string platformUserId,
            CancellationToken cancellationToken = default)
        {
            _identities.TryGetValue(GetKey(platform, platformUserId), out var identity);
            return Task.FromResult(identity);
        }

        public Task SetAsync(
            BotPlatform platform,
            string platformUserId,
            CachedIdentity identity,
            CancellationToken cancellationToken = default)
        {
            _identities[GetKey(platform, platformUserId)] = identity;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(BotPlatform platform, string platformUserId, CancellationToken cancellationToken = default)
        {
            _identities.Remove(GetKey(platform, platformUserId));
            return Task.CompletedTask;
        }

        private static string GetKey(BotPlatform platform, string platformUserId)
        {
            return $"{platform}:{platformUserId}";
        }
    }
}
