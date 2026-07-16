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

            await MoveOwnedRowsAsync(
                dbContext.NotificationSubscriptions.Where(item => item.CoreUserId == sourceUser.Id),
                item => item.CoreUserId = targetUser.Id,
                cancellationToken);

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

    private static async Task MoveOwnedRowsAsync<T>(
        IQueryable<T> query,
        Action<T> move,
        CancellationToken cancellationToken)
        where T : class
    {
        foreach (var row in await query.ToListAsync(cancellationToken))
        {
            move(row);
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
