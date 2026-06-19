using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OhMyBot.Core.Data;

public sealed class OhMyBotV2DbContextFactory : IDesignTimeDbContextFactory<OhMyBotV2DbContext>
{
    public OhMyBotV2DbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OhMyBotV2DbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ohmybot_v2;Username=ohmybot;Password=ohmybot")
            .Options;

        return new OhMyBotV2DbContext(options);
    }
}
