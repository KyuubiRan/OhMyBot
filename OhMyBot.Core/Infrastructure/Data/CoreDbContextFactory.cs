using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OhMyBot.Core.Infrastructure.Data;

public sealed class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=ohmybot_v2;Username=ohmybot;Password=ohmybot",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Core"))
            .Options;

        return new CoreDbContext(options);
    }
}
