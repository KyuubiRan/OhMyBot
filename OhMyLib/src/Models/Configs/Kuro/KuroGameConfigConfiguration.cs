using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Models.Configs.Kuro;

public class KuroGameConfigConfiguration : IEntityTypeConfiguration<KuroGameConfig>
{
    public void Configure(EntityTypeBuilder<KuroGameConfig> builder)
    {
        builder.HasIndex(x => x.KuroUserId);

        builder.HasOne(x => x.KuroUser)
               .WithMany(x => x.GameConfigs)
               .HasForeignKey(x => x.KuroUserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}