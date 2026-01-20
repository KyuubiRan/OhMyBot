using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Models.Configs.Kuro;

public class KuroUserConfig : IEntityTypeConfiguration<KuroUser>
{
    public void Configure(EntityTypeBuilder<KuroUser> builder)
    {
        builder.HasIndex(x => x.OwnerId)
               .IsUnique();
    }
}