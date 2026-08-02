using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Infrastructure.Data;

public sealed partial class CoreUserMergeService(
    CoreDbContext dbContext,
    TimeProvider timeProvider)
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public async Task MergeAsync(
        CoreUser sourceUser,
        CoreUser targetUser,
        CancellationToken cancellationToken = default)
    {
        if (sourceUser.Id == targetUser.Id)
        {
            return;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                var lowerId = Math.Min(sourceUser.Id, targetUser.Id);
                var upperId = Math.Max(sourceUser.Id, targetUser.Id);
                var lockedIds = await dbContext.Database.SqlQuery<long>($$"""
                    SELECT "Id" AS "Value"
                    FROM "CoreUsers"
                    WHERE "Id" IN ({{lowerId}}, {{upperId}})
                    ORDER BY "Id"
                    FOR UPDATE
                    """).ToArrayAsync(cancellationToken);
                if (lockedIds.Length != 2)
                {
                    throw new InvalidOperationException("待合并的 CoreUser 已不存在。");
                }

                await dbContext.Entry(sourceUser).ReloadAsync(cancellationToken);
                await dbContext.Entry(targetUser).ReloadAsync(cancellationToken);
            }

            var now = timeProvider.GetUtcNow();
            targetUser.Privilege = (OhMyBot.Contracts.Grpc.UserPrivilege)Math.Max(
                (int)targetUser.Privilege,
                (int)sourceUser.Privilege);
            targetUser.UpdatedAt = now;

            foreach (var profile in sourceUser.PlatformProfiles.ToArray())
            {
                profile.CoreUserId = targetUser.Id;
                profile.CoreUser = targetUser;
                profile.UpdatedAt = now;
                targetUser.PlatformProfiles.Add(profile);
            }

            if (dbContext.Database.IsRelational())
            {
                await MoveRegisteredPluginRowsAsync(sourceUser.Id, targetUser.Id, cancellationToken);
            }

            await MoveNotificationSubscriptionsAsync(sourceUser.Id, targetUser.Id, now, cancellationToken);

            dbContext.CoreUsers.Remove(sourceUser);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task MoveRegisteredPluginRowsAsync(
        long sourceCoreUserId,
        long targetCoreUserId,
        CancellationToken cancellationToken)
    {
        var relations = await dbContext.PluginOwnedRelations
            .AsNoTracking()
            .OrderBy(item => item.PluginId)
            .ThenBy(item => item.SchemaName)
            .ThenBy(item => item.TableName)
            .ToArrayAsync(cancellationToken);
        foreach (var relation in relations)
        {
            var schema = QuoteIdentifier(relation.SchemaName);
            var table = QuoteIdentifier(relation.TableName);
            var column = QuoteIdentifier(relation.CoreUserIdColumn);
            var sql = string.Concat(
                "UPDATE ", schema, ".", table,
                " SET ", column, " = {0} WHERE ", column, " = {1}");
            await dbContext.Database.ExecuteSqlRawAsync(
                sql,
                [targetCoreUserId, sourceCoreUserId],
                cancellationToken);
        }
    }

    /// <summary>
    /// 把 source 的订阅并到 target。<c>(CoreUserId, NotificationType, TargetId)</c> 上有唯一索引，
    /// 若两个账号订阅了同一个 (类型, 目标)——正是「两平台各订阅过一次同一角色」这种最典型的待 link 状态——
    /// 直接改写 CoreUserId 会撞唯一键使整个 /link 失败。所以命中的行合并后删源行，未命中的才改写。
    /// </summary>
    private async Task MoveNotificationSubscriptionsAsync(
        long sourceCoreUserId,
        long targetCoreUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sourceSubscriptions = await dbContext.NotificationSubscriptions
            .Where(item => item.CoreUserId == sourceCoreUserId)
            .ToListAsync(cancellationToken);
        if (sourceSubscriptions.Count == 0)
        {
            return;
        }

        var targetSubscriptions = await dbContext.NotificationSubscriptions
            .Where(item => item.CoreUserId == targetCoreUserId)
            .ToListAsync(cancellationToken);
        var targetByKey = targetSubscriptions.ToDictionary(
            item => (item.NotificationType, item.TargetId));

        foreach (var source in sourceSubscriptions)
        {
            if (!targetByKey.TryGetValue((source.NotificationType, source.TargetId), out var target))
            {
                source.CoreUserId = targetCoreUserId;
                source.UpdatedAt = now;
                continue;
            }

            // 平台开关取并集；endpoint 各平台各自「target 缺则取 source」——两侧通常正好互补（一边 TG 一边 QQ）。
            target.EnabledPlatforms |= source.EnabledPlatforms;
            target.TelegramBotInstanceId ??= source.TelegramBotInstanceId;
            target.TelegramChatId ??= source.TelegramChatId;
            target.QqBotInstanceId ??= source.QqBotInstanceId;
            target.QqChatId ??= source.QqChatId;
            target.UpdatedAt = now;
            dbContext.NotificationSubscriptions.Remove(source);
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (!IdentifierPattern().IsMatch(identifier))
        {
            throw new InvalidOperationException($"Unsafe plugin-owned relation identifier: {identifier}");
        }

        return $"\"{identifier}\"";
    }
}
