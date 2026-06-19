using Microsoft.EntityFrameworkCore;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.Data;

public class OhMyBotV2DbContext(DbContextOptions<OhMyBotV2DbContext> options) : DbContext(options)
{
    public DbSet<CoreUser> CoreUsers => Set<CoreUser>();

    public DbSet<PlatformIdentity> PlatformIdentities => Set<PlatformIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CoreUser>(builder =>
        {
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Privilege).HasConversion<string>().HasMaxLength(64);
            builder.Property(user => user.CreatedAt).IsRequired();
            builder.Property(user => user.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<PlatformIdentity>(builder =>
        {
            builder.HasKey(identity => identity.Id);
            builder.Property(identity => identity.Platform).HasConversion<string>().HasMaxLength(64);
            builder.Property(identity => identity.PlatformUserId).HasMaxLength(128).IsRequired();
            builder.Property(identity => identity.DisplayName).HasMaxLength(256);
            builder.Property(identity => identity.Username).HasMaxLength(256);
            builder.Property(identity => identity.CreatedAt).IsRequired();
            builder.Property(identity => identity.UpdatedAt).IsRequired();

            builder.HasIndex(identity => new { identity.Platform, identity.PlatformUserId }).IsUnique();
            builder.HasOne(identity => identity.CoreUser)
                   .WithMany(user => user.Identities)
                   .HasForeignKey(identity => identity.CoreUserId)
                   .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
