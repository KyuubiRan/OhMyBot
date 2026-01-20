using Microsoft.EntityFrameworkCore;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Repo;

public class OhMyDbContext : DbContext
{
    public DbSet<KuroUser> KuroUsers => Set<KuroUser>();
    public DbSet<KuroGameConfig> KuroGameConfigs => Set<KuroGameConfig>();
}