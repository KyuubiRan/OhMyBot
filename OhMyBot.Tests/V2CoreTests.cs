using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Admin;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Qq;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Infrastructure.Linking;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Infrastructure.Security;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Infrastructure.UserProfiles;

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
        StringAssert.Contains(response.TgText(), "绑定令牌");
        StringAssert.Contains(response.TgText(), tokenStore.LastToken!);
        StringAssert.Contains(response.TgText(), "有效期：5 分钟");
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
    public async Task LinkKeepsPluginRelationMetadataWhilePluginIsOffline()
    {
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-owner", "link"));
        var token = tokenStore.LastToken!;
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "ping"));
        dbContext.PluginOwnedRelations.Add(new PluginOwnedRelation
        {
            PluginId = "com.ohmybot.happytuk",
            SchemaName = "public",
            TableName = "HappytukAccounts",
            CoreUserIdColumn = "CoreUserId"
        });
        await dbContext.SaveChangesAsync();

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "link", token));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(1, await dbContext.CoreUsers.CountAsync());
        var relation = await dbContext.PluginOwnedRelations.SingleAsync();
        Assert.AreEqual("com.ohmybot.happytuk", relation.PluginId);
        Assert.AreEqual("HappytukAccounts", relation.TableName);
    }

    [TestMethod]
    public async Task LinkMergesDuplicateSubscriptionsInsteadOfViolatingUniqueIndex()
    {
        // 最典型的待 link 状态：同一个人在两个平台各订阅过同一个 (类型, 目标)。
        // (CoreUserId, NotificationType, TargetId) 上有唯一索引，若合并时直接改写 CoreUserId
        // 就会撞唯一键，整个 /link 失败并把数据库错误抛给用户。
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-owner", "link"));
        var token = tokenStore.LastToken!;
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "ping"));

        var qqUserId = (await dbContext.PlatformUserProfiles.SingleAsync(profile => profile.Uid == "qq-owner")).CoreUserId!.Value;
        var telegramUserId = (await dbContext.PlatformUserProfiles.SingleAsync(profile => profile.Uid == "tg-current")).CoreUserId!.Value;
        dbContext.NotificationSubscriptions.Add(new NotificationSubscription
        {
            CoreUserId = qqUserId,
            NotificationType = NotificationTypes.KuroAutoSign,
            TargetId = 42,
            EnabledPlatforms = (int)NotificationPlatformFlags.QQ,
            QqBotInstanceId = "qq-bot",
            QqChatId = "qq-chat"
        });
        dbContext.NotificationSubscriptions.Add(new NotificationSubscription
        {
            CoreUserId = telegramUserId,
            NotificationType = NotificationTypes.KuroAutoSign,
            TargetId = 42,
            EnabledPlatforms = (int)NotificationPlatformFlags.Telegram,
            TelegramBotInstanceId = "tg-bot",
            TelegramChatId = "tg-chat"
        });
        await dbContext.SaveChangesAsync();

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "link", token));

        Assert.AreEqual(0, response.Code);
        // 合并后该 (类型, 目标) 只剩一行，且两个平台的订阅与投递地址都保住了——
        // 用户不该因为绑定账号而丢掉任何一侧已开启的通知。
        var subscription = await dbContext.NotificationSubscriptions.SingleAsync();
        Assert.AreEqual(NotificationTypes.KuroAutoSign, subscription.NotificationType);
        Assert.AreEqual(42, subscription.TargetId);
        Assert.AreEqual((int)NotificationPlatformFlags.All, subscription.EnabledPlatforms);
        Assert.AreEqual("qq-bot", subscription.QqBotInstanceId);
        Assert.AreEqual("qq-chat", subscription.QqChatId);
        Assert.AreEqual("tg-bot", subscription.TelegramBotInstanceId);
        Assert.AreEqual("tg-chat", subscription.TelegramChatId);
    }

    [TestMethod]
    public async Task LinkMovesNonConflictingSubscriptionsToRetainedUser()
    {
        // 与上一条互补：不同目标的订阅没有唯一键冲突，应整行迁移而不是被合并掉。
        await using var dbContext = CreateDbContext();
        var tokenStore = new FakeLinkTokenStore();
        var service = CreateCommandService(dbContext, tokenStore);

        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-owner", "link"));
        var token = tokenStore.LastToken!;
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "ping"));

        var qqUserId = (await dbContext.PlatformUserProfiles.SingleAsync(profile => profile.Uid == "qq-owner")).CoreUserId!.Value;
        dbContext.NotificationSubscriptions.Add(new NotificationSubscription
        {
            CoreUserId = qqUserId,
            NotificationType = NotificationTypes.KuroAutoSign,
            TargetId = 7,
            EnabledPlatforms = (int)NotificationPlatformFlags.QQ,
            QqBotInstanceId = "qq-bot",
            QqChatId = "qq-chat"
        });
        await dbContext.SaveChangesAsync();

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-current", "link", token));

        Assert.AreEqual(0, response.Code);
        var retainedUserId = (await dbContext.CoreUsers.SingleAsync()).Id;
        var subscription = await dbContext.NotificationSubscriptions.SingleAsync();
        Assert.AreEqual(retainedUserId, subscription.CoreUserId);
        Assert.AreEqual((int)NotificationPlatformFlags.QQ, subscription.EnabledPlatforms);
    }

    [TestMethod]
    public async Task MissingOrConsumedTokenReturnsStructuredError()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-current", "link", "missing"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("LinkTokenInvalid", response.ErrorCode);
        Assert.AreEqual("绑定令牌不存在、已过期或已被使用，请重新获取。", response.QqText());
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
        StringAssert.Contains(sameUser.TgText(), "绑定令牌只能用于不同平台账号绑定。");
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
        Assert.AreEqual("绑定令牌不存在、已过期或已被使用，请重新获取。", response.QqText());
    }

    [TestMethod]
    public async Task HelpForUnknownCommandReturnsUnknownCommandMessage()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-user", "help", "doesnotexist"));

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual("未知的命令「doesnotexist」，发送 /help 查看可用命令。", response.QqText());
    }

    [TestMethod]
    public async Task HelpForLeafCommandShowsItsUsage()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore());

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-user", "help", "ping"));

        Assert.AreEqual(0, response.Code);
        StringAssert.StartsWith(response.QqText(), "/ping - ");
        StringAssert.Contains(response.QqText(), "\n用法: /ping");
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
        qqIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
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
            .Select(identity => identity.CoreUser!)
            .SingleAsync();
        var secondUser = await dbContext.PlatformUserProfiles
            .Include(identity => identity.CoreUser)
            .Where(identity => identity.Platform == BotPlatform.Qq)
            .Select(identity => identity.CoreUser!)
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
    public async Task ParentCommandWithoutArgsMatchesHelpPath()
    {
        await using var dbContext = CreateDbContext();
        var parent = new CommandDslNode
        {
            Name = "parent",
            Description = "Parent command.",
            Usage = "/parent <child>",
            Children =
            [
                CommandOnly("child", "Child command.", "/parent child", UserPrivilege.User)
            ]
        };
        var service = CreateCommandService(
            dbContext,
            new FakeLinkTokenStore(),
            CreateBuiltInCommandRegistry(extraNodes: [parent]));

        var direct = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "parent"));
        var help = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "user", "help", "parent"));

        Assert.AreEqual(help.TgText(), direct.TgText());
    }

    [TestMethod]
    public async Task RouteStoreHostedServicePublishesInitialSnapshot()
    {
        var routeStore = CreateRouteStore(CreateBuiltInCommandRegistry());
        var publisher = new FakeRouteChangePublisher();
        var hostedService = new RouteStoreHostedService(
            routeStore,
            publisher,
            Options.Create(new RouteOptions { Path = routeStore.RouteFilePath }),
            NullLogger<RouteStoreHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        var publishedVersion = await publisher.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        Assert.AreEqual(routeStore.Version, publishedVersion);
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
        Assert.AreEqual("ok", response.TgText());
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
        Assert.AreEqual("secret", response.TgText());
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
        StringAssert.StartsWith(response.TgText(), "Pong");
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
    public async Task CommandHandlerExceptionHidesInternalDetailBehindErrorId()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            new CommandDslNode
            {
                Name = "broken",
                Description = "Broken command.",
                Usage = "/broken",
                // 模拟真实底层异常：消息里带内部拓扑信息，绝不能出现在用户可见文案中。
                Handler = _ => throw new InvalidOperationException("redis offline at 10.0.0.7:6379")
            }
        ]);
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "broken"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("CommandHandlerFailed", response.ErrorCode);
        // 内部细节不外泄，但要给用户一个可用于对日志的关联 id。
        var text = response.TgText();
        StringAssert.Contains(text, "请稍后重试");
        StringAssert.Contains(text, "错误 id:");
        Assert.IsFalse(text.Contains("redis offline"), "异常原文不应出现在用户可见文案中。");
        Assert.IsFalse(text.Contains("10.0.0.7"), "内网地址不应出现在用户可见文案中。");
    }

    [TestMethod]
    public async Task CommandUserExceptionReachesUserVerbatim()
    {
        await using var dbContext = CreateDbContext();
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            new CommandDslNode
            {
                Name = "broken",
                Description = "Broken command.",
                Usage = "/broken",
                // 业务失败：处理器已经写好了给用户看的自助提示，Core 不该再折叠掉。
                Handler = _ => throw new CommandUserException("AiRouterLoginFailed", "AI Router 登录失败：用户名或密码错误")
            }
        ]);
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), registry);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "tg-1", "broken"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("AiRouterLoginFailed", response.ErrorCode);
        var text = response.TgText();
        // 用户要能据此自己解决问题：既不能被换成「请稍后重试」，也不该拿到一个只有翻日志才有意义的 id。
        StringAssert.Contains(text, "用户名或密码错误");
        Assert.IsFalse(text.Contains("错误 id:"), "业务失败不需要关联 id，给了反而暗示用户来找人查日志。");
        Assert.IsFalse(text.Contains("请稍后重试"), "重试解决不了密码错误，这句话只会让用户反复重试。");
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
        // 非管理员传入他人 uid 会被忽略，只能看到自己（"self"），且渲染文本中不含被查询的 "other"。
        StringAssert.Contains(response.TgText(), "UID: `self`");
        Assert.IsFalse(response.TgText().Contains("other", StringComparison.Ordinal));
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "target"));

        Assert.AreEqual(0, response.Code);
        // 管理员可查询他人（不同于非管理员）：渲染出目标 uid。
        StringAssert.Contains(response.TgText(), "UID: `target`");
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "@target_user"));

        Assert.AreEqual(0, response.Code);
        // 按 @username 查询解析到正确用户：渲染出目标 uid 和用户名。
        StringAssert.Contains(response.TgText(), "UID: `target`");
        StringAssert.Contains(response.TgText(), "用户名: `@target_user`");
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));

        var byUid = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "target"));
        var byUsername = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "@target_user"));

        Assert.AreEqual(0, byUid.Code);
        // 无 CoreUser 的已记录档案：权限回退为 user，显示名由姓名拼出。
        StringAssert.Contains(byUid.TgText(), "UID: `target`");
        StringAssert.Contains(byUid.TgText(), "昵称: `User Target`");
        StringAssert.Contains(byUid.TgText(), "权限: `user`");
        Assert.AreEqual(0, byUsername.Code);
        StringAssert.Contains(byUsername.TgText(), "UID: `target`");
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));
        await identityCache.SetAsync(BotPlatform.Qq, "qq-target", new CachedIdentity(telegramIdentity.CoreUserId!.Value, UserPrivilege.User));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "info", "tg-target"));

        Assert.AreEqual(0, response.Code);
        // 已绑定用户只展示当前平台（Telegram）的档案，不泄露 QQ 侧 uid。
        StringAssert.Contains(response.TgText(), "UID: `tg-target`");
        Assert.IsFalse(response.TgText().Contains("qq-target", StringComparison.Ordinal));
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminIdentity.CoreUserId!.Value, UserPrivilege.Admin));
        var infoRequest = CreateRequest(BotPlatform.Telegram, "admin", "info");
        infoRequest.ReplyToUserId = "target";

        var response = await service.ExecuteAsync(infoRequest);

        Assert.AreEqual(0, response.Code);
        // 管理员可查询回复目标用户：渲染出目标 uid。
        StringAssert.Contains(response.TgText(), "UID: `target`");
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
        adminIdentity.CoreUser!.Privilege = UserPrivilege.Admin;
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
        StringAssert.Contains(response.TgText(), "`User Target` 当前权限: `user`");
        CollectionAssert.AreEqual(
            new[] { "user", "verified-user" },
            response.TgButtonTexts().ToArray());
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
        var adminButton = panel.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "admin");
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
        Assert.AreEqual("123", response.TgSingle().EditMessageId);
        // " -> " 在 MarkdownV2 中会被转义，故分段断言权限从 user 更新到 admin。
        StringAssert.Contains(response.TgText(), "`User Target` 权限更新: `user`");
        StringAssert.Contains(response.TgText(), "`admin`");
        Assert.AreEqual(0, response.TgButtonRows().Count);
        var target = await dbContext.PlatformUserProfiles.Include(profile => profile.CoreUser).SingleAsync(profile => profile.Uid == "target");
        Assert.AreEqual(UserPrivilege.Admin, target.CoreUser!.Privilege);
    }

    [TestMethod]
    public async Task NotifyBackKeepsTypeButtonsInSameRow()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new CoreUser { Id = 1, Privilege = UserPrivilege.VerifiedUser });
        dbContext.PlatformUserProfiles.Add(new PlatformUserProfile
        {
            Platform = BotPlatform.Telegram,
            Uid = "admin",
            FirstName = "Admin",
            CoreUserId = 1
        });
        await dbContext.SaveChangesAsync();

        var identityCache = new FakeIdentityCache();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(1, UserPrivilege.VerifiedUser));
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var backPayload = await callbackStore.PutAsync(
            "notify-back",
            1,
            "chat",
            "admin",
            new NotifyBackCallbackData());
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IIdentityCache>(identityCache);
        services.AddSingleton(callbackStore);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDistributedCache, FakeDistributedCache>();
        services.AddLogging();
        services.AddSingleton<CoreIdentityService>();
        services.AddSingleton<NotificationSubscriptionService>();
        var serviceProvider = services.BuildServiceProvider();
        var notificationSources = new PluginNotificationSourceRegistry();
        notificationSources.RegisterPlugin("tests", new IPluginNotificationSource[]
        {
            new FakeNotificationSource(NotificationTypes.AiRouterAutoSign, NotificationTypes.AiRouterAutoSignDisplayName),
            new FakeNotificationSource(NotificationTypes.KuroAutoSign, NotificationTypes.KuroAutoSignDisplayName),
            new FakeNotificationSource(NotificationTypes.MihoyoAutoSign, NotificationTypes.MihoyoAutoSignDisplayName),
            new FakeNotificationSource(NotificationTypes.SklandAutoSign, NotificationTypes.SklandAutoSignDisplayName)
        });
        var callbackService = new CallbackExecutionService(
            serviceProvider.GetRequiredService<CoreIdentityService>(),
            callbackStore,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            notificationSources: notificationSources);

        var response = await callbackService.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat",
            UserId = "admin",
            MessageId = "123",
            Payload = backPayload
        });

        CollectionAssert.AreEqual(
            new[] { "AI Router 自动签到", "库街区自动签到", "米游社自动签到", "森空岛自动签到" },
            response.TgButtonTexts().ToArray());
    }

    [TestMethod]
    public async Task CallbackFromDifferentChatIsRejected()
    {
        // 按钮 payload 只在生成它的会话里有效：即便拿到 payload（例如被人从群里抄走），
        // 也不能拿到别的会话里重放。CallbackAction 存了 ChatId 就是为了这个。
        await using var dbContext = CreateDbContext();
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var harness = await CreateCallbackHarnessAsync(dbContext, callbackStore);
        var payload = await callbackStore.PutAsync(
            "notify-type-select",
            harness.CoreUserId,
            "chat-origin",
            "admin",
            new NotificationTypeCallbackData(ObservingNotificationSource.SourceType));

        var response = await harness.Service.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat-other",
            UserId = "admin",
            MessageId = "123",
            ChatType = BotChatType.Group,
            Payload = payload
        });

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("CallbackRejected", response.ErrorCode);
        Assert.IsNull(harness.Source.LastChatType, "被拒绝的回调不应进入业务 handler。");
    }

    [TestMethod]
    public async Task CallbackUserExceptionReachesUserVerbatim()
    {
        // 点按钮走的是和命令完全独立的入口，同一个「Token 已失效」不该因为入口不同就变成另一种话术。
        await using var dbContext = CreateDbContext();
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var harness = await CreateCallbackHarnessAsync(dbContext, callbackStore);
        harness.Source.ThrowOnBuild = new CommandUserException("KuroTokenExpired", "Token 已失效，请重新绑定库街区账号");
        var payload = await callbackStore.PutAsync(
            "notify-type-select",
            harness.CoreUserId,
            "chat",
            "admin",
            new NotificationTypeCallbackData(ObservingNotificationSource.SourceType));

        var response = await harness.Service.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat",
            UserId = "admin",
            MessageId = "123",
            ChatType = BotChatType.Private,
            Payload = payload
        });

        Assert.AreEqual("KuroTokenExpired", response.ErrorCode);
        StringAssert.Contains(response.TgText(), "请重新绑定库街区账号");
    }

    [TestMethod]
    public async Task CallbackHandlerExceptionHidesInternalDetailBehindErrorId()
    {
        // 这里不兜底的话异常会穿到 gRPC：用户拿到的是网关现编的 id，而 Core 日志里根本没有那个 id，
        // 报上来的 id 就永远查不到。所以 errorId 必须由抓到异常的这一层生成并落日志。
        await using var dbContext = CreateDbContext();
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var harness = await CreateCallbackHarnessAsync(dbContext, callbackStore);
        harness.Source.ThrowOnBuild = new InvalidOperationException("redis offline at 10.0.0.7:6379");
        var payload = await callbackStore.PutAsync(
            "notify-type-select",
            harness.CoreUserId,
            "chat",
            "admin",
            new NotificationTypeCallbackData(ObservingNotificationSource.SourceType));

        var response = await harness.Service.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat",
            UserId = "admin",
            MessageId = "123",
            ChatType = BotChatType.Private,
            Payload = payload
        });

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("CallbackHandlerFailed", response.ErrorCode);
        var text = response.TgText();
        StringAssert.Contains(text, "错误 id:");
        Assert.IsFalse(text.Contains("10.0.0.7"), "内网地址不应出现在用户可见文案中。");
    }

    [TestMethod]
    public async Task CallbackChatTypeIsPassedThroughToHandler()
    {
        // 回调过去被硬编码成私聊，群里点按钮的 handler 会拿到错误的会话类型。
        // 插件据此决定能否在群里展示内容，所以必须透传真实值。
        await using var dbContext = CreateDbContext();
        var callbackStore = new CallbackActionStore(new FakeDistributedCache(), Options.Create(new CallbackActionOptions()));
        var harness = await CreateCallbackHarnessAsync(dbContext, callbackStore);
        var payload = await callbackStore.PutAsync(
            "notify-type-select",
            harness.CoreUserId,
            "chat",
            "admin",
            new NotificationTypeCallbackData(ObservingNotificationSource.SourceType));

        var response = await harness.Service.ExecuteAsync(new CallbackRequest
        {
            Platform = BotPlatform.Telegram,
            ChatId = "chat",
            UserId = "admin",
            MessageId = "123",
            ChatType = BotChatType.Group,
            Payload = payload
        });

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(BotChatType.Group, harness.Source.LastChatType);
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

        Assert.IsFalse(response.TgButtonTexts().Contains("admin"));
    }

    [TestMethod]
    public async Task SetPrivilegeCreatesTargetWhenUidUnknown()
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
        var adminProfile = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "admin");
        adminProfile.CoreUser!.Privilege = UserPrivilege.Owner;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminProfile.CoreUserId!.Value, UserPrivilege.Owner));

        // 目标 999999 从未发过消息、库里没有任何档案。setpriv 不应报“未找到”，而应给出权限选择。
        var panel = await commandService.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "setpriv", "999999"));

        Assert.AreEqual(0, panel.Code);
        var adminButton = panel.TgButtonRows().SelectMany(row => row.Buttons).Single(button => button.Text == "admin");
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

        // 点选权限那一刻才就地建档：新目标以所选权限落库，后续其发消息再补齐昵称/用户名。
        Assert.AreEqual(0, response.Code);
        var target = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "999999");
        Assert.AreEqual(UserPrivilege.Admin, target.CoreUser!.Privilege);
    }

    [TestMethod]
    public async Task SetPrivilegeRejectsUnknownNonNumericTarget()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "ping"));
        var adminProfile = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "admin");
        adminProfile.CoreUser!.Privilege = UserPrivilege.Owner;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Telegram, "admin", new CachedIdentity(adminProfile.CoreUserId!.Value, UserPrivilege.Owner));

        // 非数字目标（且无 @）：拿不到真实 uid，仍报“未找到”，避免凭空建脏档；不落库。
        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "admin", "setpriv", "notauser"));

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("UserNotFound", response.ErrorCode);
        Assert.IsFalse(await dbContext.PlatformUserProfiles.AnyAsync(profile => profile.Uid == "notauser"));
    }

    [TestMethod]
    public async Task QqSetPrivilegeUnknownUidBuildsNumberedMenu()
    {
        await using var dbContext = CreateDbContext();
        var identityCache = new FakeIdentityCache();
        var service = CreateCommandService(dbContext, new FakeLinkTokenStore(), identityCache: identityCache);
        await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-admin", "ping"));
        var adminProfile = await dbContext.PlatformUserProfiles
            .Include(profile => profile.CoreUser)
            .SingleAsync(profile => profile.Uid == "qq-admin");
        adminProfile.CoreUser!.Privilege = UserPrivilege.Owner;
        await dbContext.SaveChangesAsync();
        await identityCache.SetAsync(BotPlatform.Qq, "qq-admin", new CachedIdentity(adminProfile.CoreUserId!.Value, UserPrivilege.Owner));

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Qq, "qq-admin", "setpriv", "888888"));

        // QQ 无原生按钮：命令产出 Telegram 形态按钮，经 QqMenuConverter 在 gRPC 边界转成回复序号的编号菜单，
        // 从而让 QQ 侧 setpriv 真正可改权限（而非过去的只读）。
        var converter = new QqMenuConverter(new QqMenuStore(new FakeDistributedCache(), Options.Create(new QqMenuOptions())));
        var qq = await converter.ToQqAsync(response, BotChatType.Private);

        var menu = qq.Qq.Messages.Single();
        Assert.IsFalse(string.IsNullOrEmpty(menu.MenuToken));
        StringAssert.Contains(menu.Text, "1. ");
        StringAssert.Contains(menu.Text, "admin");
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

        Assert.Contains("32 字节", exception.Message);
    }

    [TestMethod]
    public async Task NotificationSubscriptionsDefaultDisabledAndToggleOnlyCurrentPlatform()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new Core.Infrastructure.Data.Entities.CoreUser { Id = 1 });
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

        Assert.IsFalse(defaultEnabled.Contains(100));
        Assert.IsTrue(telegramAfterToggle.Contains(100));
        Assert.IsFalse(qqAfterTelegramToggle.Contains(100));

        var subscription = await dbContext.NotificationSubscriptions.SingleAsync();
        Assert.AreEqual((int)NotificationPlatformFlags.Telegram, subscription.EnabledPlatforms);
        Assert.AreEqual("tg", subscription.TelegramBotInstanceId);
        Assert.AreEqual("chat", subscription.TelegramChatId);
    }

    [TestMethod]
    public async Task NotificationSubscriptionEnableTurnsOnCurrentPlatformAndStoresEndpoint()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new Core.Infrastructure.Data.Entities.CoreUser { Id = 1 });
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
        Assert.AreEqual((int)NotificationPlatformFlags.Telegram, subscription.EnabledPlatforms);
        Assert.AreEqual("tg-new", subscription.TelegramBotInstanceId);
        Assert.AreEqual("chat-new", subscription.TelegramChatId);
    }

    [TestMethod]
    public async Task ListEnabledDeliveriesReturnsEveryEnabledPlatform()
    {
        // Regression: scheduled auto-sign tasks used to query Telegram only, so QQ subscribers
        // never received push notifications even after enabling them. Deliveries must cover both.
        await using var dbContext = CreateDbContext();
        dbContext.CoreUsers.Add(new Core.Infrastructure.Data.Entities.CoreUser { Id = 1 });
        await dbContext.SaveChangesAsync();
        var service = new NotificationSubscriptionService(dbContext, TimeProvider.System);

        await service.EnableAsync(
            1,
            BotPlatform.Telegram,
            "tg",
            "tg-chat",
            NotificationTypes.MihoyoAutoSign,
            100,
            CancellationToken.None);
        await service.EnableAsync(
            1,
            BotPlatform.Qq,
            "qq",
            "qq-chat",
            NotificationTypes.MihoyoAutoSign,
            100,
            CancellationToken.None);

        var deliveries = await service.ListEnabledDeliveriesByTargetAsync(
            NotificationTypes.MihoyoAutoSign,
            100,
            CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { BotPlatform.Telegram, BotPlatform.Qq },
            deliveries.Select(delivery => delivery.Platform).ToArray());
        var qq = deliveries.Single(delivery => delivery.Platform == BotPlatform.Qq);
        Assert.AreEqual("qq", qq.BotInstanceId);
        Assert.AreEqual("qq-chat", qq.ChatId);
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
    public async Task RouteFileCannotWidenCodeDeclaredPlatforms()
    {
        // 插件通过 SupportedPlatforms 声明自己能在哪些平台被调用，那是安全边界的一部分：
        // 一个只声明 Telegram 的命令若能靠改 route.json 在 QQ 跑起来，就绕过了该声明
        // （且它的回调仍会被插件包装层拒绝，形成「命令能跑、按钮报错」的半残状态）。
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly(
                "tgonly",
                "Telegram only command.",
                "/tgonly",
                UserPrivilege.User,
                platforms: SupportedPlatforms.Telegram)
        ]);

        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "tgonly",
                    CoreCommand = "tgonly",
                    SupportPlatforms = ["Telegram", "QQ"]
                }
            ]
        };

        var routeStore = CreateRouteStore(registry, routeDocument);
        await routeStore.InitializeAsync();

        Assert.IsTrue(routeStore.TryGet("tgonly", out var route));
        Assert.AreEqual(SupportedPlatforms.Telegram, route.SupportPlatforms);
        Assert.IsFalse(routeStore.GetRoutes(BotPlatform.Qq).Any(item => item.Command == "tgonly"));
    }

    [TestMethod]
    public async Task RouteFileCanStillNarrowCodeDeclaredPlatforms()
    {
        // 收紧方向必须保持可用：route.json 的既有用途就是按部署关掉某些平台。
        var registry = CreateBuiltInCommandRegistry(extraNodes:
        [
            CommandOnly("both", "Both platforms.", "/both", UserPrivilege.User, platforms: SupportedPlatforms.All)
        ]);

        var routeDocument = new RouteDocument
        {
            Routes =
            [
                new RouteDefinition
                {
                    Command = "both",
                    CoreCommand = "both",
                    SupportPlatforms = ["Telegram"]
                }
            ]
        };

        var routeStore = CreateRouteStore(registry, routeDocument);
        await routeStore.InitializeAsync();

        Assert.IsTrue(routeStore.TryGet("both", out var route));
        Assert.AreEqual(SupportedPlatforms.Telegram, route.SupportPlatforms);
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
        // 帮助文本现由 Core 按 Telegram MarkdownV2 渲染，故预期子串需同样转义。
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("可用命令："), StringComparison.Ordinal));
        Assert.Contains(MarkdownV2.Escape("/help - 显示可用指令"), response.TgText());
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("/help - 显示可用指令，目前支持的子命令有"), StringComparison.Ordinal));
        Assert.Contains(MarkdownV2.Escape("/ping"), response.TgText());
        Assert.Contains(MarkdownV2.Escape("/link"), response.TgText());
        Assert.Contains(MarkdownV2.Escape("/ai - AI 相关指令"), response.TgText());
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("目前支持的子命令有"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("/ai_router_auto_signin"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("子命令：ai_router"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("/owner"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("权限："), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HelpCommandShowsAiRouterGroupForVerifiedUser()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai"));

        Assert.AreEqual(0, response.Code);
        Assert.Contains(MarkdownV2.Escape("router - Router 平台相关指令"), response.TgText());
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("/ai_router_auto_signin"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("权限："), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HelpCommandShowsAiRouterCommandsInSubCommand()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);

        var response = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help", "ai", "router"));

        Assert.AreEqual(0, response.Code);
        Assert.Contains(MarkdownV2.Escape("bind - 绑定用户"), response.TgText());
        Assert.Contains(MarkdownV2.Escape("autosign - 自动签到管理"), response.TgText());
        Assert.Contains(MarkdownV2.Escape("delete - 删除绑定"), response.TgText());
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("/ping"), StringComparison.Ordinal));
        Assert.IsFalse(response.TgText().Contains(MarkdownV2.Escape("权限："), StringComparison.Ordinal));
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

        Assert.AreEqual(help.TgText(), ai.TgText());
        Assert.AreEqual(routerHelp.TgText(), router.TgText());
    }

    [TestMethod]
    public async Task AiRouterParentIsPrivateOnlyBecauseAllSubcommandsArePrivate()
    {
        // ai/router 两个父节点自身没标私聊，只有叶子命令是私聊。父命令的有效会话类型应由子命令聚合，
        // 于是群里触发 /ai 必须被拦下并提示只能私聊——否则整棵 ai 子树在群里形同暴露。
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);
        var request = CreateRequest(BotPlatform.Telegram, "verified", "ai");
        request.ChatType = BotChatType.Group;

        var response = await service.ExecuteAsync(request);

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual("UnsupportedChatType", response.ErrorCode);
        Assert.Contains("只能在私聊中使用", response.TgText());
    }

    [TestMethod]
    public async Task HelpHidesAiRouterParentInGroupChatButShowsItInPrivate()
    {
        // 子命令全私聊 => 父命令在群聊 /help 里应自动隐藏（可用会话类型聚合后不含群聊），私聊里照常出现。
        await using var dbContext = CreateDbContext();
        var service = CreateHelpCommandService(dbContext, "verified", UserPrivilege.VerifiedUser);
        var groupRequest = CreateRequest(BotPlatform.Telegram, "verified", "help");
        groupRequest.ChatType = BotChatType.Group;

        var groupHelp = await service.ExecuteAsync(groupRequest);
        var privateHelp = await service.ExecuteAsync(CreateRequest(BotPlatform.Telegram, "verified", "help"));

        Assert.AreEqual(0, groupHelp.Code);
        Assert.IsFalse(groupHelp.TgText().Contains(MarkdownV2.Escape("/ai - AI 相关指令"), StringComparison.Ordinal));
        Assert.Contains(MarkdownV2.Escape("/ping"), groupHelp.TgText());
        Assert.Contains(MarkdownV2.Escape("/ai - AI 相关指令"), privateHelp.TgText());
    }

    [TestMethod]
    public void ParentChatTypeRestrictionCapsChildrenSubtree()
    {
        // father 大于一切：父命令限制为私聊时，其下的群聊子命令被卡成不可达(None)，
        // 而非仍以自身的 Group 生效；父命令本身聚合后为 Private。
        var father = new CommandDslNode
        {
            Name = "father",
            Description = "Father.",
            Usage = "/father",
            SupportChatTypes = SupportedChatTypes.Private,
            Children =
            [
                CommandOnly("grp", "Group child.", "/father grp", UserPrivilege.User, chatTypes: SupportedChatTypes.Group),
                CommandOnly("prv", "Private child.", "/father prv", UserPrivilege.User, chatTypes: SupportedChatTypes.Private)
            ]
        };
        var registry = CreateBuiltInCommandRegistry(extraNodes: [father]);
        var routeStore = CreateRouteStore(registry);
        routeStore.InitializeAsync().GetAwaiter().GetResult();

        Assert.IsTrue(routeStore.TryGet("father", out var fatherRoute));
        Assert.AreEqual(SupportedChatTypes.Private, fatherRoute.EffectiveSupportChatTypes);

        Assert.IsTrue(routeStore.TryGetNode(["father", "grp"], out var groupChild));
        Assert.AreEqual(SupportedChatTypes.None, groupChild.SupportChatTypes);

        Assert.IsTrue(routeStore.TryGetNode(["father", "prv"], out var privateChild));
        Assert.AreEqual(SupportedChatTypes.Private, privateChild.SupportChatTypes);
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
        Assert.IsFalse(aiHelp.TgText().Contains("autosign", StringComparison.Ordinal));
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
        Assert.AreEqual(UserPrivilege.Owner, identity.CoreUser!.Privilege);

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
        Assert.AreEqual(UserPrivilege.VerifiedUser, identity.CoreUser!.Privilege);

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
    public async Task PushMessageAdminCommandPublishesPrivateMessage()
    {
        var publisher = new FakeNotificationPublisher();
        var executor = new AdminCommandExecutor(new AdminCommandCatalog([
            new PushMessageAdminCommand(publisher)
        ]));

        var result = await executor.ExecuteAsync("pushmsg -p telegram -uid 123456 -m \"hello world\"");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(BotPlatform.Telegram, publisher.Platform);
        Assert.AreEqual("123456", publisher.ChatId);
        CollectionAssert.AreEqual(new[] { "hello world" }, publisher.Messages.ToArray());
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

    [TestMethod]
    public async Task InteractiveConsoleOutputQueueBroadcastsToAllReaders()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var queue = new InteractiveConsoleOutputQueue();
        await using var first = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await using var second = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var firstRead = first.MoveNextAsync().AsTask();
        var secondRead = second.MoveNextAsync().AsTask();

        Assert.IsTrue(queue.TryEnqueue(new InteractiveConsoleOutputItem([new ConsoleTextSegment("hello")])));
        Assert.IsTrue(await firstRead.WaitAsync(cts.Token));
        Assert.IsTrue(await secondRead.WaitAsync(cts.Token));
        Assert.AreEqual("hello", first.Current.Segments[0].Text);
        Assert.AreEqual("hello", second.Current.Segments[0].Text);
    }

    private static CoreDbContext CreateDbContext()
    {
        return new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static CommandExecutionService CreateCommandService(
        CoreDbContext dbContext,
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
        CoreDbContext? dbContext = null,
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
        services.AddSingleton<IPlatformCommandDslProvider>(
            new StaticDslProvider([CreateAiRouterDslNode()]));
        services.AddSingleton<IPlatformCommandDslProvider, NotificationCommandDslProvider>();
        if (extraNodes is { Count: > 0 })
        {
            services.AddSingleton<IPlatformCommandDslProvider>(new StaticDslProvider(extraNodes));
        }

        var serviceProvider = services.BuildServiceProvider();
        return new PlatformCommandDslRegistry(serviceProvider.GetRequiredService<IEnumerable<IPlatformCommandDslProvider>>());
    }

    private static ServiceProvider CreateCallbackServiceProvider(
        CoreDbContext dbContext,
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
        CoreDbContext dbContext,
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
        services.AddSingleton<IPlatformCommandDslProvider>(
            new StaticDslProvider([CreateAiRouterDslNode()]));
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
        CoreDbContext dbContext,
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

    private sealed class FakeRouteChangePublisher : IRouteChangePublisher
    {
        public TaskCompletionSource<long> Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default)
        {
            Published.TrySetResult(version);
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

    private sealed class FakeNotificationPublisher : INotificationPublisher
    {
        public BotPlatform Platform { get; private set; }
        public string BotInstanceId { get; private set; } = string.Empty;
        public string ChatId { get; private set; } = string.Empty;
        public IReadOnlyList<string> Messages { get; private set; } = [];

        public Task PublishAsync(
            BotPlatform platform,
            string botInstanceId,
            string chatId,
            IReadOnlyList<string> messages,
            CancellationToken cancellationToken = default)
        {
            Platform = platform;
            BotInstanceId = botInstanceId;
            ChatId = chatId;
            Messages = messages;
            return Task.CompletedTask;
        }

        public Task PublishTelegramAsync(
            string botInstanceId,
            string chatId,
            IReadOnlyList<string> messages,
            CancellationToken cancellationToken = default)
        {
            return PublishAsync(BotPlatform.Telegram, botInstanceId, chatId, messages, cancellationToken);
        }
    }

    private sealed record CallbackHarness(
        CallbackExecutionService Service,
        ObservingNotificationSource Source,
        long CoreUserId);

    /// <summary>
    /// 组装一个最小回调执行环境：一个已建档的用户 + 一个记录调用上下文的通知来源，
    /// 供「会话校验」「会话类型透传」这类只关心 CallbackExecutionService 行为的测试复用。
    /// </summary>
    private static async Task<CallbackHarness> CreateCallbackHarnessAsync(
        CoreDbContext dbContext,
        CallbackActionStore callbackStore)
    {
        const long coreUserId = 1;
        dbContext.CoreUsers.Add(new CoreUser { Id = coreUserId, Privilege = UserPrivilege.VerifiedUser });
        dbContext.PlatformUserProfiles.Add(new PlatformUserProfile
        {
            Platform = BotPlatform.Telegram,
            Uid = "admin",
            FirstName = "Admin",
            CoreUserId = coreUserId
        });
        await dbContext.SaveChangesAsync();

        var identityCache = new FakeIdentityCache();
        await identityCache.SetAsync(
            BotPlatform.Telegram,
            "admin",
            new CachedIdentity(coreUserId, UserPrivilege.VerifiedUser));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IIdentityCache>(identityCache);
        services.AddSingleton(callbackStore);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDistributedCache, FakeDistributedCache>();
        services.AddLogging();
        services.AddSingleton<CoreIdentityService>();
        services.AddSingleton<NotificationSubscriptionService>();
        var serviceProvider = services.BuildServiceProvider();

        var source = new ObservingNotificationSource();
        var notificationSources = new PluginNotificationSourceRegistry();
        notificationSources.RegisterPlugin("tests", [source]);

        var service = new CallbackExecutionService(
            serviceProvider.GetRequiredService<CoreIdentityService>(),
            callbackStore,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            notificationSources: notificationSources);

        return new CallbackHarness(service, source, coreUserId);
    }

    /// <summary>记录 handler 实际收到的会话类型，用于断言回调上下文没有被伪造成私聊。</summary>
    private sealed class ObservingNotificationSource : IPluginNotificationSource
    {
        public const string SourceType = "tests-observing";

        public string Type => SourceType;

        public string DisplayName => "测试来源";

        public int Order => 1;

        public BotChatType? LastChatType { get; private set; }

        /// <summary>非 null 时 <see cref="BuildAccountPanelAsync"/> 直接抛出它，用于验证回调路径的兜底。</summary>
        public Exception? ThrowOnBuild { get; set; }

        public Task<bool> HasEnabledTargetsAsync(
            CommandContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<CommandResponse> BuildAccountPanelAsync(
            CommandContext context,
            string? editMessageId,
            CancellationToken cancellationToken = default)
        {
            LastChatType = context.Request.ChatType;
            if (ThrowOnBuild is not null)
            {
                throw ThrowOnBuild;
            }

            return Task.FromResult(CommandResponses.Silent(context));
        }

        public Task<CommandResponse> ToggleAsync(
            CommandContext context,
            long accountId,
            bool toggleAll,
            string editMessageId,
            CancellationToken cancellationToken = default)
        {
            LastChatType = context.Request.ChatType;
            return Task.FromResult(CommandResponses.Silent(context));
        }
    }

    private sealed class FakeNotificationSource(string type, string displayName) : IPluginNotificationSource
    {
        public string Type { get; } = type;

        public string DisplayName { get; } = displayName;

        public int Order { get; } = type switch
        {
            NotificationTypes.AiRouterAutoSign => 100,
            NotificationTypes.KuroAutoSign => 200,
            NotificationTypes.MihoyoAutoSign => 300,
            NotificationTypes.SklandAutoSign => 400,
            _ => 500
        };

        public Task<bool> HasEnabledTargetsAsync(
            CommandContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<CommandResponse> BuildAccountPanelAsync(
            CommandContext context,
            string? editMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResponses.Silent(context));

        public Task<CommandResponse> ToggleAsync(
            CommandContext context,
            long accountId,
            bool toggleAll,
            string editMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResponses.Silent(context));
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

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;
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

    private static CommandDslNode CreateAiRouterDslNode()
    {
        return new CommandDslNode
        {
            Name = "ai",
            Description = "AI 相关指令",
            Usage = "/ai",
            Children =
            [
                new CommandDslNode
                {
                    Name = "router",
                    Description = "Router 平台相关指令",
                    Usage = "/ai router",
                    Children =
                    [
                        CommandOnly(
                            "bind",
                            "绑定用户",
                            "/ai router bind",
                            UserPrivilege.VerifiedUser,
                            chatTypes: SupportedChatTypes.Private),
                        CommandOnly(
                            "autosign",
                            "自动签到管理",
                            "/ai router autosign",
                            UserPrivilege.VerifiedUser,
                            chatTypes: SupportedChatTypes.Private),
                        CommandOnly(
                            "delete",
                            "删除绑定",
                            "/ai router delete",
                            UserPrivilege.VerifiedUser,
                            chatTypes: SupportedChatTypes.Private)
                    ]
                }
            ]
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
