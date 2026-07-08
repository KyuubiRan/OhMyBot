using Microsoft.EntityFrameworkCore;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Infrastructure.Data;

public class OhMyBotV2DbContext(DbContextOptions<OhMyBotV2DbContext> options) : DbContext(options)
{
    public DbSet<CoreUser> CoreUsers => Set<CoreUser>();

    public DbSet<PlatformUserProfile> PlatformUserProfiles => Set<PlatformUserProfile>();

    public DbSet<AiRouterAccount> AiRouterAccounts => Set<AiRouterAccount>();

    public DbSet<KuroAccount> KuroAccounts => Set<KuroAccount>();

    public DbSet<KuroGameRole> KuroGameRoles => Set<KuroGameRole>();

    public DbSet<MihoyoAccount> MihoyoAccounts => Set<MihoyoAccount>();

    public DbSet<MihoyoGameRole> MihoyoGameRoles => Set<MihoyoGameRole>();

    public DbSet<SklandAccount> SklandAccounts => Set<SklandAccount>();

    public DbSet<SklandGameRole> SklandGameRoles => Set<SklandGameRole>();

    public DbSet<NotificationSubscription> NotificationSubscriptions => Set<NotificationSubscription>();

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

        modelBuilder.Entity<KuroAccount>(builder =>
        {
            builder.HasKey(account => account.Id);
            builder.Property(account => account.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(account => account.TokenCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.DevCode).HasMaxLength(255);
            builder.Property(account => account.DistinctId).HasMaxLength(255);
            builder.Property(account => account.CreatedAt).IsRequired();
            builder.Property(account => account.UpdatedAt).IsRequired();

            builder.HasIndex(account => account.BbsUserId).IsUnique();
            builder.HasOne(account => account.CoreUser)
                   .WithMany(user => user.KuroAccounts)
                   .HasForeignKey(account => account.CoreUserId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KuroGameRole>(builder =>
        {
            builder.HasKey(role => role.Id);
            builder.Property(role => role.GameName).HasMaxLength(128).IsRequired();
            builder.Property(role => role.ServerId).HasMaxLength(128).IsRequired();
            builder.Property(role => role.ServerName).HasMaxLength(128).IsRequired();
            builder.Property(role => role.RoleName).HasMaxLength(256).IsRequired();
            builder.Property(role => role.GameLevel).HasMaxLength(64).IsRequired();
            builder.Property(role => role.CreatedAt).IsRequired();
            builder.Property(role => role.UpdatedAt).IsRequired();

            builder.HasIndex(role => new { role.KuroAccountId, role.GameId, role.ServerId, role.RoleId }).IsUnique();
            builder.HasOne(role => role.KuroAccount)
                   .WithMany(account => account.Roles)
                   .HasForeignKey(role => role.KuroAccountId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MihoyoAccount>(builder =>
        {
            builder.HasKey(account => account.Id);
            builder.Property(account => account.Region);
            builder.Property(account => account.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(account => account.CookieCiphertext).HasMaxLength(4096).IsRequired();
            builder.Property(account => account.StokenCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.Mid).HasMaxLength(255).IsRequired();
            builder.Property(account => account.CreatedAt).IsRequired();
            builder.Property(account => account.UpdatedAt).IsRequired();

            builder.HasIndex(account => new { account.Region, account.Stuid }).IsUnique();
            builder.HasOne(account => account.CoreUser)
                   .WithMany(user => user.MihoyoAccounts)
                   .HasForeignKey(account => account.CoreUserId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MihoyoGameRole>(builder =>
        {
            builder.HasKey(role => role.Id);
            builder.Property(role => role.GameBiz).HasMaxLength(64).IsRequired();
            builder.Property(role => role.GameName).HasMaxLength(128).IsRequired();
            builder.Property(role => role.Region).HasMaxLength(128).IsRequired();
            builder.Property(role => role.Nickname).HasMaxLength(256).IsRequired();
            builder.Property(role => role.Level).HasMaxLength(64).IsRequired();
            builder.Property(role => role.CreatedAt).IsRequired();
            builder.Property(role => role.UpdatedAt).IsRequired();

            builder.HasIndex(role => new { role.MihoyoAccountId, role.GameBiz, role.GameUid }).IsUnique();
            builder.HasOne(role => role.MihoyoAccount)
                   .WithMany(account => account.Roles)
                   .HasForeignKey(role => role.MihoyoAccountId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SklandAccount>(builder =>
        {
            builder.HasKey(account => account.Id);
            builder.Property(account => account.SklandUserId).HasMaxLength(128).IsRequired();
            builder.Property(account => account.DeviceId).HasMaxLength(64).IsRequired();
            builder.Property(account => account.DisplayName).HasMaxLength(256).IsRequired();
            builder.Property(account => account.HgTokenCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.CredCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.SignTokenCiphertext).HasMaxLength(2048).IsRequired();
            builder.Property(account => account.CreatedAt).IsRequired();
            builder.Property(account => account.UpdatedAt).IsRequired();

            builder.HasIndex(account => account.SklandUserId).IsUnique();
            builder.HasOne(account => account.CoreUser)
                   .WithMany(user => user.SklandAccounts)
                   .HasForeignKey(account => account.CoreUserId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SklandGameRole>(builder =>
        {
            builder.HasKey(role => role.Id);
            builder.Property(role => role.AppCode).HasMaxLength(64).IsRequired();
            builder.Property(role => role.GameName).HasMaxLength(128).IsRequired();
            builder.Property(role => role.Uid).HasMaxLength(128).IsRequired();
            builder.Property(role => role.NickName).HasMaxLength(256).IsRequired();
            builder.Property(role => role.Level).HasMaxLength(64).IsRequired();
            builder.Property(role => role.ChannelName).HasMaxLength(128).IsRequired();
            builder.Property(role => role.ServerId).HasMaxLength(128).IsRequired();
            builder.Property(role => role.RoleId).HasMaxLength(128).IsRequired();
            builder.Property(role => role.CreatedAt).IsRequired();
            builder.Property(role => role.UpdatedAt).IsRequired();

            builder.HasIndex(role => new { role.SklandAccountId, role.GameId, role.Uid, role.RoleId }).IsUnique();
            builder.HasOne(role => role.SklandAccount)
                   .WithMany(account => account.Roles)
                   .HasForeignKey(role => role.SklandAccountId)
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
