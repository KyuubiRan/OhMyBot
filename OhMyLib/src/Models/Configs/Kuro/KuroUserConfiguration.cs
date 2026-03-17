using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Models.Configs.Kuro;

public class KuroUserConfiguration : IEntityTypeConfiguration<KuroUser>
{
    public void Configure(EntityTypeBuilder<KuroUser> builder)
    {
        builder.HasOne(x => x.OwnerBotUser)
               .WithOne(x => x.KuroUser)
               .HasForeignKey<KuroUser>(x => x.OwnerUserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BbsUserId)
               .IsUnique();
    }
}