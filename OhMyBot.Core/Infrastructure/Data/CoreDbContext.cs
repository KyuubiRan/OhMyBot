using Microsoft.EntityFrameworkCore;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Infrastructure.Data;

/// <summary>
/// Core-owned model. Plugin tables intentionally do not belong to this context.
/// </summary>
public class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    public DbSet<CoreUser> CoreUsers => Set<CoreUser>();

    public DbSet<PlatformUserProfile> PlatformUserProfiles => Set<PlatformUserProfile>();

    public DbSet<NotificationSubscription> NotificationSubscriptions => Set<NotificationSubscription>();

    public DbSet<PluginOwnedRelation> PluginOwnedRelations => Set<PluginOwnedRelation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CoreUser>(builder =>
        {
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Privilege);
            builder.Property(user => user.CreatedAt).IsRequired();
            builder.Property(user => user.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<PlatformUserProfile>(builder =>
        {
            builder.HasKey(profile => profile.Id);
            builder.Property(profile => profile.Platform);
            builder.Property(profile => profile.Uid).HasMaxLength(128).IsRequired();
            builder.Property(profile => profile.Username).HasMaxLength(256);
            builder.Property(profile => profile.FirstName).HasMaxLength(256);
            builder.Property(profile => profile.LastName).HasMaxLength(256);
            builder.Property(profile => profile.Nickname).HasMaxLength(256);
            builder.Property(profile => profile.CreatedAt).IsRequired();
            builder.Property(profile => profile.UpdatedAt).IsRequired();

            builder.HasIndex(profile => new { profile.Platform, profile.Uid }).IsUnique();
            builder.HasOne(profile => profile.CoreUser)
                .WithMany(user => user.PlatformProfiles)
                .HasForeignKey(profile => profile.CoreUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NotificationSubscription>(builder =>
        {
            builder.HasKey(subscription => subscription.Id);
            builder.Property(subscription => subscription.NotificationType).HasMaxLength(128).IsRequired();
            builder.Property(subscription => subscription.TelegramBotInstanceId).HasMaxLength(128);
            builder.Property(subscription => subscription.TelegramChatId).HasMaxLength(128);
            builder.Property(subscription => subscription.QqBotInstanceId).HasMaxLength(128);
            builder.Property(subscription => subscription.QqChatId).HasMaxLength(128);
            builder.Property(subscription => subscription.CreatedAt).IsRequired();
            builder.Property(subscription => subscription.UpdatedAt).IsRequired();

            builder.HasIndex(subscription => new
            {
                subscription.CoreUserId,
                subscription.NotificationType,
                subscription.TargetId
            }).IsUnique();
            builder.HasOne(subscription => subscription.CoreUser)
                .WithMany(user => user.NotificationSubscriptions)
                .HasForeignKey(subscription => subscription.CoreUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PluginOwnedRelation>(builder =>
        {
            builder.HasKey(relation => new
            {
                relation.SchemaName,
                relation.TableName,
                relation.CoreUserIdColumn
            });
            builder.Property(relation => relation.PluginId).HasMaxLength(255).IsRequired();
            builder.Property(relation => relation.SchemaName).HasMaxLength(63).IsRequired();
            builder.Property(relation => relation.TableName).HasMaxLength(63).IsRequired();
            builder.Property(relation => relation.CoreUserIdColumn).HasMaxLength(63).IsRequired();
            builder.HasIndex(relation => relation.PluginId);

        });
    }
}
