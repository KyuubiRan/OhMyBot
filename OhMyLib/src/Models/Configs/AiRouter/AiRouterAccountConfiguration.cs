using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OhMyLib.Models.AiRouter;

namespace OhMyLib.Models.Configs.AiRouter;

public class AiRouterAccountConfiguration : IEntityTypeConfiguration<AiRouterAccount>
{
    public void Configure(EntityTypeBuilder<AiRouterAccount> builder)
    {
        builder.HasOne(x => x.OwnerBotUser)
               .WithMany(x => x.AiRouterAccounts)
               .HasForeignKey(x => x.OwnerUserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.Account)
               .IsUnique();
    }
}
