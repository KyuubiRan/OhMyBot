using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Admin;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Linking;
using OhMyBot.Core.Notifications;
using OhMyBot.Core.Routing;
using OhMyBot.Core.Security;
using OhMyBot.Core.UserProfiles;

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
        Assert.AreEqual(1, await dbContext.PlatformUserProfiles.CountAsync());
        Assert.AreEqual("tester", identity.Username);
        Assert.IsNotNull(identity.CoreUserId);
    }

    [TestMethod]
    public async Task UserProfileRecordCreatesProfileWithoutCoreUser()
    {
        await using var dbContext = CreateDbContext();
        var service = new PlatformUserProfileService(dbContext, new FakeUserProfileCache(), TimeProvider.System);

        await service.RecordAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "10001",
            Username = "tester",
            FirstName = "Test",
            LastName = "User"
        });

        Assert.AreEqual(0, await dbContext.CoreUsers.CountAsync());

        var profile = await dbContext.PlatformUserProfiles.SingleAsync();
        Assert.IsNull(profile.CoreUserId);
        Assert.AreEqual(BotPlatform.Telegram, profile.Platform);
        Assert.AreEqual("10001", profile.Uid);
        Assert.AreEqual("tester", profile.Username);
        Assert.AreEqual("Test", profile.FirstName);
        Assert.AreEqual("User", profile.LastName);
        Assert.IsNull(profile.Nickname);
    }

    [TestMethod]
    public async Task UserProfileRecordUpdatesDatabaseWhenProfileChanges()
    {
        await using var dbContext = CreateDbContext();
        var cache = new FakeUserProfileCache();
        var service = new PlatformUserProfileService(dbContext, cache, TimeProvider.System);

        await service.RecordAsync(new CommandRequest
        {
            Platform = BotPlatform.Qq,
            UserId = "10001",
            Nickname = "old"
        });
        await service.RecordAsync(new CommandRequest
        {
            Platform = BotPlatform.Qq,
            UserId = "10001",
            Nickname = "new"
        });

        Assert.AreEqual(1, await dbContext.PlatformUserProfiles.CountAsync());
        var profile = await dbContext.PlatformUserProfiles.SingleAsync();
        Assert.AreEqual("new", profile.Nickname);
    }

    [TestMethod]
    public async Task UserProfileRecordCreatesDatabaseRowWhenOnlyCacheMatches()
    {
        await using var dbContext = CreateDbContext();
        var cache = new FakeUserProfileCache();
        await cache.SetAsync(new UserProfileCacheEntry(
            new UserProfileUpdate(
                BotPlatform.Telegram,
                "10001",
                "tester",
                "Test",
                "User",
                null),
            Persisted: false));
        var service = new PlatformUserProfileService(dbContext, cache, TimeProvider.System);

        await service.RecordAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "10001",
            Username = "tester",
            FirstName = "Test",
            LastName = "User"
        });

        var profile = await dbContext.PlatformUserProfiles.SingleAsync();
        Assert.IsNull(profile.CoreUserId);
        Assert.AreEqual("10001", profile.Uid);
    }

    [TestMethod]
    public async Task UserProfileRecordSkipsDatabaseWhenPersistedCacheMatches()
    {
        await using var dbContext = CreateDbContext();
        var cache = new FakeUserProfileCache();
        await cache.SetAsync(UserProfileCacheEntry.PersistedProfile(new UserProfileUpdate(
            BotPlatform.Telegram,
            "10001",
            "tester",
            "Test",
            "User",
            null)));
        var service = new PlatformUserProfileService(dbContext, cache, TimeProvider.System);

        await service.RecordAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "10001",
            Username = "tester",
            FirstName = "Test",
            LastName = "User"
        });

        Assert.AreEqual(0, await dbContext.PlatformUserProfiles.CountAsync());
    }

    [TestMethod]
    public async Task LinkWithoutArgsWritesTokenWithFiveMinuteTtl()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "link"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.LinkToken, response.DataKind);
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

        Assert.AreEqual(0, response.Code);
        Assert.IsFalse(tokenStore.Tokens.ContainsKey(token));
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        Assert.AreEqual(2, await dbContext.PlatformUserProfiles.CountAsync());
    }

    [TestMethod]
    public async Task MissingOrConsumedTokenReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-current", "link", "missing"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("LinkTokenInvalid", response.ErrorCode);
        Assert.AreEqual("绑定令牌不存在、已过期或已被使用，请重新获取。", response.Message);
    }

    [TestMethod]
    public async Task LinkTokenCannotBeConsumedFromSamePlatform()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link"));
        var token = tokenStore.LastToken!;

        var sameUser = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link", token));
        var otherUser = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-other", "link", token));

        Assert.AreNotEqual(0, sameUser.Code);
        Assert.AreEqual("LinkPlatformNotAllowed", sameUser.ErrorCode);
        Assert.AreEqual("绑定令牌只能用于不同平台账号绑定。", sameUser.Message);
        Assert.AreNotEqual(0, otherUser.Code);
        Assert.AreEqual("LinkPlatformNotAllowed", otherUser.ErrorCode);
        Assert.IsTrue(tokenStore.Tokens.ContainsKey(token));
        Assert.AreEqual(2, await dbContext.CoreUsers.CountAsync());
    }

    [TestMethod]
    public async Task ConsumedLinkTokenReturnsChineseError()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link"));
        var token = tokenStore.LastToken!;
        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-current", "link", token));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-other", "link", token));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("LinkTokenInvalid", response.ErrorCode);
        Assert.AreEqual("绑定令牌不存在、已过期或已被使用，请重新获取。", response.Message);
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

        var qqIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Platform == BotPlatform.Qq);
        qqIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();

        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-owner", "link"));
        var token = tokenStore.LastToken!;

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-admin", "link", token));

        Assert.AreEqual(0, response.Code);
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

        var firstUser = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .Where(identity => identity.Platform == BotPlatform.Telegram)
            .Select(identity => identity.CoreUser)
            .SingleAsync();
        var secondUser = await dbContext.PlatformUserProfiles
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

        Assert.AreEqual(0, response.Code);
        var retainedUser = await dbContext.CoreUsers.SingleAsync();
        Assert.AreEqual(Math.Min(firstUser.Id, secondUser.Id), retainedUser.Id);
        Assert.AreEqual(2, await dbContext.PlatformUserProfiles.CountAsync(identity => identity.CoreUserId == retainedUser.Id));
    }

    [TestMethod]
    public void CommandListIsFilteredBySupportedPlatforms()
    {
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("qqonly", "QQ only command.", "/qqonly", UserPrivilege.User, SupportedPlatforms.QQ)
        ]);
        var routeStore = CreateRouteStore(registry);
        routeStore.InitializeAsync().GetAwaiter().GetResult();

        var telegramRoutes = routeStore.GetRoutes(BotPlatform.Telegram);
        var qqRoutes = routeStore.GetRoutes(BotPlatform.Qq);

        Assert.IsFalse(telegramRoutes.Any(route => route.Command == "qqonly"));
        Assert.IsTrue(qqRoutes.Any(route => route.Command == "qqonly"));
    }

    [TestMethod]
    public async Task UnsupportedChatTypeReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("group", "Group only command.", "/group", UserPrivilege.User, chatTypes: SupportedChatTypes.Group)
        ]);

        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "group"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("UnsupportedChatType", response.ErrorCode);
    }

    [TestMethod]
    public async Task SupportedGroupChatTypeExecutesCommand()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("group", "Group only command.", "/group", UserPrivilege.User, chatTypes: SupportedChatTypes.Group)
        ]);

        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);
        var request = CreateRequest(BotPlatform.Telegram, "tg-1", "group");
        request.ChatType = BotChatType.Group;

        var response = await service.ExecuteAsync(request);

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual("ok", response.Text.Text);
    }

    [TestMethod]
    public async Task InsufficientPrivilegeReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("owner", "Owner command.", "/owner", UserPrivilege.Owner, handlerText: "secret")
        ]);

        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "owner"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("PrivilegeDenied", response.ErrorCode);
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

        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("owner", "Owner command.", "/owner", UserPrivilege.Owner, handlerText: "secret")
        ]);

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

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual("secret", response.Text.Text);
        var profile = await dbContext.PlatformUserProfiles.SingleAsync();
        Assert.IsNull(profile.CoreUserId);
        Assert.AreEqual("tg-owner", profile.Uid);
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
                    Command = "ping",
                    CoreCommand = "ping",
                    Description = "Ping alias.",
                    Usage = "/p",
                    Aliases = ["p"],
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    Enabled = true
                }
            ]
        };
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), routeDocument: routeDocument);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "p"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.Ping, response.DataKind);
    }

    [TestMethod]
    public async Task RouteTargetMissingReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            new CommandDslNode
            {
                Name = "broken",
                Description = "Broken route.",
                Usage = "/broken"
            }
        ]);
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "broken"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("RouteTargetMissing", response.ErrorCode);
    }

    [TestMethod]
    public async Task CommandHandlerExceptionReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            new CommandDslNode
            {
                Name = "broken",
                Description = "Broken command.",
                Usage = "/broken",
                Handler = _ => throw new InvalidOperationException("redis offline")
            }
        ]);
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "broken"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("CommandHandlerFailed", response.ErrorCode);
        Assert.Contains("redis offline", response.Message);
    }

    [TestMethod]
    public async Task RouteCannotLowerCorePrivilege()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("owner", "Owner command.", "/owner", UserPrivilege.Owner, handlerText: "secret")
        ]);

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

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("PrivilegeDenied", response.ErrorCode);
    }

    [TestMethod]
    public async Task InfoCommandIgnoresUidForNonAdminAndHidesCoreUserId()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "self", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "other", "ping"));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "self", "info", "other"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.UserInfo, response.DataKind);
        Assert.IsFalse(response.UserInfo.HasCoreUserId);
        Assert.AreEqual("self", response.UserInfo.Identities.Single().Uid);
    }

    [TestMethod]
    public async Task InfoCommandAdminCanQueryOtherUserAndSeeCoreUserId()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "target", "ping"));
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "target"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.UserInfo, response.DataKind);
        Assert.IsTrue(response.UserInfo.HasCoreUserId);
        Assert.IsTrue(response.UserInfo.CoreUserId > 0);
        Assert.AreEqual("target", response.UserInfo.Identities.Single().Uid);
        Assert.AreEqual(BotPlatform.Telegram, response.UserInfo.Identities.Single().Platform);
    }

    [TestMethod]
    public async Task InfoCommandAdminCanQueryOtherUserByUsername()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        var targetRequest = CreateRequest(BotPlatform.Telegram, "target", "ping");
        targetRequest.Username = "target_user";
        await service.ExecuteAsync(targetRequest);
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "@target_user"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.UserInfo, response.DataKind);
        Assert.AreEqual("target", response.UserInfo.Identities.Single().Uid);
    }

    [TestMethod]
    public async Task InfoCommandAdminCanQueryRecordedProfileWithoutIdentity()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        dbContext.PlatformUserProfiles.Add(new PlatformUserProfile
        {
            Platform = BotPlatform.Telegram,
            Uid = "target",
            Username = "target_user",
            FirstName = "Target",
            LastName = "User",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var byUid = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "target"));
        var byUsername = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "@target_user"));

        Assert.AreEqual(0, byUid.Code);
        Assert.AreEqual(CommandResponseDataKind.UserInfo, byUid.DataKind);
        Assert.IsFalse(byUid.UserInfo.HasCoreUserId);
        Assert.AreEqual(UserPrivilege.User, byUid.UserInfo.Privilege);
        Assert.AreEqual("target", byUid.UserInfo.Identities.Single().Uid);
        Assert.AreEqual("User Target", byUid.UserInfo.Identities.Single().DisplayName);
        Assert.AreEqual(0, byUsername.Code);
        Assert.AreEqual("target", byUsername.UserInfo.Identities.Single().Uid);
    }

    [TestMethod]
    public async Task InfoCommandReturnsCurrentPlatformProfileForLinkedUser()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-target", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-target", "ping"));
        var telegramIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Platform == BotPlatform.Telegram && identity.Uid == "tg-target");
        var qqIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Platform == BotPlatform.Qq && identity.Uid == "qq-target");
        qqIdentity.CoreUserId = telegramIdentity.CoreUserId;
        qqIdentity.CoreUser = telegramIdentity.CoreUser;
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));
        await identityCache.SetAsync(BotPlatform.Qq, "qq-target", new CachedIdentity(telegramIdentity.CoreUserId!.Value, UserPrivilege.User));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "tg-target"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(1, response.UserInfo.Identities.Count);
        Assert.AreEqual(BotPlatform.Telegram, response.UserInfo.Identities.Single().Platform);
        Assert.AreEqual("tg-target", response.UserInfo.Identities.Single().Uid);
    }

    [TestMethod]
    public async Task InfoCommandAdminCanQueryReplyTarget()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "target", "ping"));
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));
        var infoRequest = CreateRequest(BotPlatform.Telegram, "admin", "info");
        infoRequest.ReplyToUserId = "target";

        var response = await service.ExecuteAsync(infoRequest);

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(CommandResponseDataKind.UserInfo, response.DataKind);
        Assert.AreEqual("target", response.UserInfo.Identities.Single().Uid);
    }

    [TestMethod]
    public async Task InfoCommandAdminQueryMissingUserReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .SingleAsync(identity => identity.Uid == "admin");
        adminIdentity.CoreUser.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "missing"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("UserNotFound", response.ErrorCode);
    }

    [TestMethod]
    public async Task SetPrivilegeCommandBuildsAdminButtonsForTarget()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await service.ExecuteAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "target",
            Command = "ping",
            Username = "target_user",
            FirstName = "Target",
            LastName = "User",
            ChatType = BotChatType.Private
        });
        var adminIdentity = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "admin");
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "setpriv", "@target_user"));

        Assert.AreEqual(0, response.Code);
        Assert.Contains("`User Target` 当前权限: `user`", response.Text.Text);
        CollectionAssert.AreEqual(
            new[] { "user", "verified-user" },
            response.ButtonRows.SelectMany(row => row.Buttons).Select(button => button.Text).ToArray());
    }

    [TestMethod]
    public async Task SetPrivilegeCallbackUpdatesPrivilegeAndEditsMessage()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var serviceProvider = CreateCallbackServiceProvider(dbContext, identityCache, callbackStore);
        var commandService = CreateCommandService(
            dbContext,
            new FakeLinkTokenStore(),
            registry: CreateBuiltInCommandRegistry(dbContext, new FakeLinkTokenStore(), identityCache, callbackStore),
            identityCache: identityCache);
        await commandService.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await commandService.ExecuteAsync(new CommandRequest
        {
            Platform = BotPlatform.Telegram,
            UserId = "target",
            Command = "ping",
            FirstName = "Target",
            LastName = "User",
            ChatType = BotChatType.Private
        });
        var adminProfile = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "admin");
        adminProfile.CoreUser!.Privilege = UserPrivilege.Owner;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminProfile.CoreUserId!.Value, UserPrivilege.Owner));
        var panel = await commandService.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "setpriv", "target"));
        var adminButton = panel.ButtonRows.SelectMany(row => row.Buttons).Single(button => button.Text == "admin");
        var callbackService = new CallbackExecutionService(
            serviceProvider.GetRequiredService<CoreIdentityService>(),
            callbackStore,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);

        var response = await callbackService.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat",
            UserId = "admin",
            MessageId = "123",
            Payload = adminButton.Payload
        });

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual("123", response.EditMessageId);
        Assert.Contains("`User Target` 权限更新: `user` -> `admin`", response.Text.Text);
        Assert.AreEqual(0, response.ButtonRows.Count);
        var target = await dbContext.PlatformUserProfiles.Include(profile => profile.CoreUser).SingleAsync(profile => profile.Uid == "target");
        Assert.AreEqual(UserPrivilege.Admin, target.CoreUser!.Privilege);
    }

    [TestMethod]
    public async Task SetPrivilegeAdminCannotSetAdmin()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "target", "ping"));
        var adminProfile = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "admin");
        adminProfile.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminProfile.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "setpriv", "target"));

        Assert.IsFalse(response.ButtonRows.SelectMany(row => row.Buttons).Any(button => button.Text == "admin"));
    }

    [TestMethod]
    public void AesSecretProtectorRoundTripsWithoutPlaintext()
    {
        var protector = new AesGcmSecretProtector(Options.Create(new EncryptionOptions
        {
            Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())
        }));

        var ciphertext = protector.Protect("secret-password");
        var plaintext = protector.Unprotect(ciphertext);

        Assert.AreEqual("secret-password", plaintext);
        Assert.StartsWith("v1:", ciphertext);
        Assert.IsFalse(ciphertext.Contains("secret-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AesSecretProtectorRejectsInvalidKey()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new AesGcmSecretProtector(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String([1, 2, 3]) })));

        Assert.Contains("exactly 32 bytes", exception.Message);
    }

    [TestMethod]
    public async Task NotificationSubscriptionsDefaultEnabledAndToggleOnlyCurrentPlatform()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new Core.Data.Entities.CoreUser { Id = 1 });
        await dbContext.SaveChangesAsync();
        var service = new NotificationSubscriptionService(dbContext, TimeProvider.System);

        var defaultEnabled = await service.GetEnabledTargetIdsAsync(
            1,
            BotPlatform.Telegram,
            NotificationTypes.AiRouterAutoSign,
            [100],
            CancellationToken.None);
        await service.ToggleAsync(
            1,
            BotPlatform.Telegram,
            "tg",
            "chat",
            NotificationTypes.AiRouterAutoSign,
            100,
            CancellationToken.None);
        var telegramAfterToggle = await service.GetEnabledTargetIdsAsync(
            1,
            BotPlatform.Telegram,
            NotificationTypes.AiRouterAutoSign,
            [100],
            CancellationToken.None);
        var qqAfterTelegramToggle = await service.GetEnabledTargetIdsAsync(
            1,
            BotPlatform.Qq,
            NotificationTypes.AiRouterAutoSign,
            [100],
            CancellationToken.None);

        Assert.IsTrue(defaultEnabled.Contains(100));
        Assert.IsFalse(telegramAfterToggle.Contains(100));
        Assert.IsTrue(qqAfterTelegramToggle.Contains(100));

        var subscription = await dbContext.NotificationSubscriptions.SingleAsync();
        Assert.AreEqual((int)NotificationPlatformFlags.QQ, subscription.EnabledPlatforms);
        Assert.AreEqual("tg", subscription.TelegramBotInstanceId);
        Assert.AreEqual("chat", subscription.TelegramChatId);
    }

    [TestMethod]
    public async Task NotificationSubscriptionEnableTurnsOnCurrentPlatformAndStoresEndpoint()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new Core.Data.Entities.CoreUser { Id = 1 });
        await dbContext.SaveChangesAsync();
        var service = new NotificationSubscriptionService(dbContext, TimeProvider.System);

        await service.ToggleAsync(
            1,
            BotPlatform.Telegram,
            "tg-old",
            "chat-old",
            NotificationTypes.AiRouterAutoSign,
            100,
            CancellationToken.None);
        await service.EnableAsync(
            1,
            BotPlatform.Telegram,
            "tg-new",
            "chat-new",
            NotificationTypes.AiRouterAutoSign,
            100,
            CancellationToken.None);

        var enabled = await service.GetEnabledTargetIdsAsync(
            1,
            BotPlatform.Telegram,
            NotificationTypes.AiRouterAutoSign,
            [100],
            CancellationToken.None);
        var subscription = await dbContext.NotificationSubscriptions.SingleAsync();

        Assert.IsTrue(enabled.Contains(100));
        Assert.AreEqual((int)NotificationPlatformFlags.All, subscription.EnabledPlatforms);
        Assert.AreEqual("tg-new", subscription.TelegramBotInstanceId);
        Assert.AreEqual("chat-new", subscription.TelegramChatId);
    }

    [TestMethod]
    public async Task NotifyAccountPanelAddsBackButtonBesideToggleAll()
    {
        await using var dbContext = CreateDbContext();
        var callbackStore = new CallbackActionStore(
            new FakeDistributedCache(),
            Options.Create(new CallbackActionOptions()));
        var builder = new AiRouterResponseBuilder(
            callbackStore,
            new NotificationSubscriptionService(dbContext, TimeProvider.System),
            TimeProvider.System);
        var context = new CommandContext(
            CreateRequest(BotPlatform.Telegram, "tg-1", "notify"),
            new ResolvedIdentity(1, UserPrivilege.VerifiedUser, BotPlatform.Telegram, "tg-1"),
            TimeProvider.System.GetTimestamp(),
            CancellationToken.None);

        var response = await builder.BuildNotifyAccountPanelAsync(
            context,
            [
                new AiRouterAccount
                {
                    Id = 100,
                    CoreUserId = 1,
                    DisplayName = "Account1",
                    LoginEmail = "a@example.com"
                }
            ],
            cancellationToken: CancellationToken.None);

        var lastRow = response.ButtonRows.Last();
        CollectionAssert.AreEqual(
            new[] { "开启/关闭全部", "返回" },
            lastRow.Buttons.Select(button => button.Text).ToArray());
        Assert.StartsWith("[开] ", response.ButtonRows[0].Buttons[0].Text);
    }

    [TestMethod]
    public async Task InvalidRouteJsonKeepsPreviousSnapshot()
    {
        var registry = CreateBuiltInCommandRegistry();
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
    public async Task RouteFileIsGeneratedAndMergedWithoutOverwritingExistingRoutes()
    {
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("owner", "Owner command.", "/owner", UserPrivilege.Owner, handlerText: "secret")
        ]);

        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "ping",
                    CoreCommand = "ping",
                    Description = "Custom ping.",
                    Usage = "/custom-ping",
                    RequiredPrivilege = "Owner",
                    SupportPlatforms = ["Telegram"],
                    Enabled = false
                }
            ]
        };

        var routeStore = CreateRouteStore(registry, routeDocument);
        await routeStore.InitializeAsync();

        var json = await File.ReadAllTextAsync(routeStore.RouteFilePath);
        var generated = JsonSerializer.Deserialize<RouteDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.IsNotNull(generated);
        var pingRoute = generated.Routes.Single(route => route.Command == "ping");
        Assert.AreEqual("Custom ping.", pingRoute.Description);
        Assert.AreEqual("/custom-ping", pingRoute.Usage);
        Assert.AreEqual("Owner", pingRoute.RequiredPrivilege);
        Assert.IsFalse(pingRoute.Enabled);
        Assert.IsTrue(generated.Routes.Any(route => route.Command == "link"));
        Assert.IsTrue(generated.Routes.Any(route => route.Command == "owner"));
    }

    [TestMethod]
    public async Task HelpCommandShowsOnlyCommandsAllowedForUserPrivilege()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "user", UserPrivilege.User);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "help"));

        Assert.AreEqual(0, response.Code);
        Assert.IsFalse(response.Text.Text.Contains("可用命令：", StringComparison.Ordinal));
        Assert.Contains("/help - 显示可用指令", response.Text.Text);
        Assert.IsFalse(response.Text.Text.Contains("/help - 显示可用指令，目前支持的子命令有", StringComparison.Ordinal));
        Assert.Contains("/ping", response.Text.Text);
        Assert.Contains("/link", response.Text.Text);
        Assert.Contains("/ai - AI 相关指令", response.Text.Text);
        Assert.IsFalse(response.Text.Text.Contains("目前支持的子命令有", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("/ai_router_auto_signin", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("子命令：ai_router", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("/owner", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("权限：", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HelpCommandShowsAiRouterGroupForVerifiedUser()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai"));

        Assert.AreEqual(0, response.Code);
        Assert.Contains("router - Router 平台相关指令", response.Text.Text);
        Assert.IsFalse(response.Text.Text.Contains("/ai_router_auto_signin", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("权限：", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HelpCommandShowsAiRouterCommandsInSubCommand()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai", "router"));

        Assert.AreEqual(0, response.Code);
        Assert.Contains("bind - 绑定用户", response.Text.Text);
        Assert.Contains("autosign - 自动签到管理", response.Text.Text);
        Assert.Contains("delete - 删除绑定", response.Text.Text);
        Assert.IsFalse(response.Text.Text.Contains("/ping", StringComparison.Ordinal));
        Assert.IsFalse(response.Text.Text.Contains("权限：", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CategoryCommandWithoutSubCommandShowsSameHelp()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);

        var help = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai"));
        var ai = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "ai"));
        var routerHelp = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai", "router"));
        var router = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "ai", "router"));

        Assert.AreEqual(help.Text.Text, ai.Text.Text);
        Assert.AreEqual(routerHelp.Text.Text, router.Text.Text);
    }

    [TestMethod]
    public async Task StartIsNotHelpAlias()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "user", UserPrivilege.User);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "start"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("RouteNotFound", response.ErrorCode);
    }

    [TestMethod]
    public async Task HelpCommandUsesEffectivePrivilegeWhenRouteAttemptsToLowerCommand()
    {
        await using var dbContext = CreateDbContext();
        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "help",
                    CoreCommand = "help",
                    Description = "Help.",
                    Usage = "/help",
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    SupportChatTypes = ["Private"],
                    Enabled = true
                },
                new RouteDefinition
                {
                    Command = "ai",
                    CoreCommand = "ai",
                    Description = "AI",
                    Usage = "/ai",
                    RequiredPrivilege = "User",
                    SupportPlatforms = ["Telegram"],
                    SupportChatTypes = ["Private"],
                    Enabled = true,
                    Children =
                    [
                        new RouteDefinition
                        {
                            Command = "router",
                            CoreCommand = "router",
                            Description = "Router",
                            Usage = "/ai router",
                            RequiredPrivilege = "User",
                            SupportPlatforms = ["Telegram"],
                            SupportChatTypes = ["Private"],
                            Enabled = true,
                            Children =
                            [
                                new RouteDefinition
                                {
                                    Command = "autosign",
                                    CoreCommand = "autosign",
                                    Description = "Auto sign.",
                                    Usage = "/ai router autosign",
                                    RequiredPrivilege = "User",
                                    SupportPlatforms = ["Telegram"],
                                    SupportChatTypes = ["Private"],
                                    Enabled = true
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var service = CreateHelpCommandService(dbContext, "user", UserPrivilege.User, routeDocument);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "help"));

        Assert.AreEqual(0, response.Code);
        var aiHelp = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "help", "ai", "router"));
        Assert.IsFalse(aiHelp.Text.Text.Contains("autosign", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AdminUserPrivilegeCommandCreatesIdentityAndRefreshesCache()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var executor = CreateAdminCommandExecutor(dbContext, identityCache);

        var result = await executor.ExecuteAsync("user -p telegram -uid 123456 -sp owner");

        Assert.IsTrue(result.Success);
        var identity = await dbContext.PlatformUserProfiles
            .Include(item => item.CoreUser)
            .SingleAsync(item => item.Platform == BotPlatform.Telegram && item.Uid == "123456");
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
        var executor = CreateAdminCommandExecutor(dbContext, identityCache);

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
    public async Task AdminUserPrivilegeCommandSupportsVerifiedUser()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var executor = CreateAdminCommandExecutor(dbContext, identityCache);

        var result = await executor.ExecuteAsync("user -p telegram -uid 123456 -sp verified-user");

        Assert.IsTrue(result.Success);
        Assert.Contains("privilege=verified-user", result.Message);

        var identity = await dbContext.PlatformUserProfiles
            .Include(item => item.CoreUser)
            .SingleAsync(item => item.Platform == BotPlatform.Telegram && item.Uid == "123456");
        Assert.AreEqual(UserPrivilege.VerifiedUser, identity.CoreUser.Privilege);

        var cached = await identityCache.GetAsync(BotPlatform.Telegram, "123456");
        Assert.IsNotNull(cached);
        Assert.AreEqual(UserPrivilege.VerifiedUser, cached.Privilege);
    }

    [TestMethod]
    public async Task AdminUserQueryReadsDatabaseIdentity()
    {
        await using var dbContext = CreateDbContext();
        var executor = CreateAdminCommandExecutor(dbContext, new FakeIdentityCache());
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
        var executor = CreateAdminCommandExecutor(dbContext, identityCache);
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
        var executor = CreateAdminCommandExecutor(dbContext, new FakeIdentityCache());

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
    public async Task AdminCommandAliasDispatchesToCommand()
    {
        var executor = new AdminCommandExecutor(new AdminCommandCatalog([
            new FakeAdminCommand("echo", ["say"])
        ]));

        var result = await executor.ExecuteAsync("say hello");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("echo: hello", result.Message);
    }

    [TestMethod]
    public async Task AdminUserCommandReturnsErrorsForInvalidInput()
    {
        await using var dbContext = CreateDbContext();
        var executor = CreateAdminCommandExecutor(dbContext, new FakeIdentityCache());

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
        Assert.Contains("Invalid privilege. Supported values: user, verified-user, admin, owner.", invalidPrivilege.Message);
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
        PlatformCommandDslRegistry? registry = null,
        RouteDocument? routeDocument = null,
        FakeIdentityCache? identityCache = null)
    {
        identityCache ??= new FakeIdentityCache();
        registry ??= CreateBuiltInCommandRegistry(dbContext, tokenStore, identityCache);
        var routeStore = CreateRouteStore(registry, routeDocument);
        routeStore.InitializeAsync().GetAwaiter().GetResult();

        return new CommandExecutionService(
            new CoreIdentityService(dbContext, identityCache, TimeProvider.System),
            new PlatformUserProfileService(dbContext, new FakeUserProfileCache(), TimeProvider.System),
            routeStore,
            new PlatformCommandDslExecutor(routeStore),
            NullLogger<CommandExecutionService>.Instance,
            TimeProvider.System);
    }

    private static PlatformCommandDslRegistry CreateBuiltInCommandRegistry(
        OhMyBotV2DbContext? dbContext = null,
        FakeLinkTokenStore? tokenStore = null,
        FakeIdentityCache? identityCache = null,
        CallbackActionStore? callbackStore = null,
        IReadOnlyList<CommandDslNode>? extraNodes = null)
    {
        dbContext ??= CreateDbContext();
        tokenStore ??= new FakeLinkTokenStore();
        identityCache ??= new FakeIdentityCache();
        callbackStore ??= new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<ILinkTokenStore>(tokenStore);
        services.AddSingleton<IIdentityCache>(identityCache);
        services.AddSingleton(callbackStore);
        services.AddSingleton<SetPrivilegeService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new LinkTokenOptions()));
        services.AddSingleton<CoreIdentityService>();
        services.AddSingleton<IPlatformCommandDslProvider, CoreCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, AiRouterCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, NotificationCommandDslProvider>();
        if (extraNodes is { Count: > 0 })
        {
            services.AddSingleton<IPlatformCommandDslProvider>(new StaticDslProvider(extraNodes));
        }

        var serviceProvider = services.BuildServiceProvider();
        return new PlatformCommandDslRegistry(serviceProvider.GetRequiredService<IEnumerable<IPlatformCommandDslProvider>>());
    }

    private static ServiceProvider CreateCallbackServiceProvider(
        OhMyBotV2DbContext dbContext,
        FakeIdentityCache identityCache,
        CallbackActionStore callbackStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IIdentityCache>(identityCache);
        services.AddSingleton(callbackStore);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CoreIdentityService>();
        services.AddSingleton<SetPrivilegeService>();
        return services.BuildServiceProvider();
    }

    private static CommandExecutionService CreateHelpCommandService(
        OhMyBotV2DbContext dbContext,
        string userId,
        UserPrivilege privilege,
        RouteDocument? routeDocument = null)
    {
        var identityCache = new FakeIdentityCache();
        identityCache.SetAsync(BotPlatform.Telegram, userId, new CachedIdentity(1, privilege))
            .GetAwaiter()
            .GetResult();

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IIdentityCache>(identityCache);
        services.AddSingleton<ILinkTokenStore>(new FakeLinkTokenStore());
        services.AddSingleton(new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions())));
        services.AddSingleton<SetPrivilegeService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new LinkTokenOptions()));
        services.AddSingleton<CoreIdentityService>();
        services.AddSingleton<IPlatformCommandDslProvider, CoreCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider, AiRouterCommandDslProvider>();
        services.AddSingleton<IPlatformCommandDslProvider>(new StaticDslProvider(
        [
            CommandOnly("owner", "Owner command.", "/owner", UserPrivilege.Owner)
        ]));
        services.AddSingleton<PlatformCommandDslRegistry>();
        services.AddSingleton(provider => CreateRouteStore(provider.GetRequiredService<PlatformCommandDslRegistry>(), routeDocument));
        services.AddSingleton<PlatformCommandDslExecutor>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<RouteStore>().InitializeAsync().GetAwaiter().GetResult();
        return new CommandExecutionService(
            provider.GetRequiredService<CoreIdentityService>(),
            new PlatformUserProfileService(dbContext, new FakeUserProfileCache(), TimeProvider.System),
            provider.GetRequiredService<RouteStore>(),
            provider.GetRequiredService<PlatformCommandDslExecutor>(),
            NullLogger<CommandExecutionService>.Instance,
            TimeProvider.System);
    }

    private static AdminCommandExecutor CreateAdminCommandExecutor(
        OhMyBotV2DbContext dbContext,
        FakeIdentityCache identityCache)
    {
        var commands = new IAdminCommand[]
        {
            new UserAdminCommand(dbContext, identityCache, TimeProvider.System)
        };

        return new AdminCommandExecutor(new AdminCommandCatalog(commands));
    }

    private static RouteStore CreateRouteStore(PlatformCommandDslRegistry registry, RouteDocument? routeDocument = null)
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
            ChatType = BotChatType.Private,
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

    private sealed class FakeUserProfileCache : IUserProfileCache
    {
        private readonly Dictionary<string, UserProfileCacheEntry> _profiles = new(StringComparer.Ordinal);

        public Task<UserProfileCacheEntry?> GetAsync(
            BotPlatform platform,
            string uid,
            CancellationToken cancellationToken = default)
        {
            _profiles.TryGetValue(GetKey(platform, uid), out var profile);
            return Task.FromResult(profile);
        }

        public Task SetAsync(UserProfileCacheEntry entry, CancellationToken cancellationToken = default)
        {
            _profiles[GetKey(entry.Profile.Platform, entry.Profile.Uid)] = entry;
            return Task.CompletedTask;
        }

        private static string GetKey(BotPlatform platform, string uid)
        {
            return $"{platform}:{uid}";
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

    private sealed class FakeAdminCommand(string name, IReadOnlyList<string> aliases) : IAdminCommand
    {
        public AdminCommandDefinition Definition { get; } = new(
            name,
            $"{name} [args]",
            "Fake admin command.",
            aliases,
            [],
            []);

        public Task<AdminCommandResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AdminCommandResult.Ok($"{name}: {string.Join(' ', args)}"));
        }
    }

    private static CommandDslNode CommandOnly(
        string name,
        string description,
        string usage,
        UserPrivilege requiredPrivilege,
        SupportedPlatforms platforms = SupportedPlatforms.All,
        SupportedChatTypes chatTypes = SupportedChatTypes.All,
        string handlerText = "ok")
    {
        return new CommandDslNode
        {
            Name = name,
            Description = description,
            Usage = usage,
            RequiredPrivilege = requiredPrivilege,
            SupportPlatforms = platforms,
            SupportChatTypes = chatTypes,
            Handler = context => Task.FromResult(CommandResponses.Text(handlerText, context))
        };
    }

    private sealed class StaticDslProvider(IReadOnlyList<CommandDslNode> nodes) : IPlatformCommandDslProvider
    {
        public IEnumerable<CommandDslNode> GetNodes()
        {
            return nodes;
        }
    }
}
