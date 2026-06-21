using Microsoft.EntityFrameworkCore;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.Data;

public class OhMyBotV2DbContext(DbContextOptions<OhMyBotV2DbContext> options) : DbContext(options)
{
    public DbSet<CoreUser> CoreUsers => Set<CoreUser>();

    public DbSet<PlatformIdentity> PlatformIdentities => Set<PlatformIdentity>();

    public DbSet<PlatformUserProfile> PlatformUserProfiles => Set<PlatformUserProfile>();

    public DbSet<AiRouterAccount> AiRouterAccounts => Set<AiRouterAccount>();

    public DbSet<NotificationSubscription> NotificationSubscriptions => Set<NotificationSubscription>();

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

        modelBuilder.Entity<PlatformUserProfile>(builder =>
        {
            builder.HasKey(profile => profile.Id);
            builder.Property(profile => profile.Platform).HasConversion<string>().HasMaxLength(64);
            builder.Property(profile => profile.Uid).HasMaxLength(128).IsRequired();
            builder.Property(profile => profile.Username).HasMaxLength(256);
            builder.Property(profile => profile.FirstName).HasMaxLength(256);
            builder.Property(profile => profile.LastName).HasMaxLength(256);
            builder.Property(profile => profile.Nickname).HasMaxLength(256);
            builder.Property(profile => profile.CreatedAt).IsRequired();
            builder.Property(profile => profile.UpdatedAt).IsRequired();

            builder.HasIndex(profile => new { profile.Platform, profile.Uid }).IsUnique();
        });

        modelBuilder.Entity<AiRouterAccount>(builder =>
        {
            builder.HasKey(account => account.Id);
            builder.Property(account => account.LoginEmail).HasMaxLength(320).IsRequired();
            builder.Property(account => account.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(account => account.PasswordCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.CreatedAt).IsRequired();
            builder.Property(account => account.UpdatedAt).IsRequired();

            builder.HasIndex(account => account.LoginEmail).IsUnique();
            builder.HasOne(account => account.CoreUser)
                   .WithMany(user => user.AiRouterAccounts)
                   .HasForeignKey(account => account.CoreUserId)
                   .OnDelete(DeleteBehavior.Cascade);
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
    }
}
