using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OhMyLib.Models.Common;

namespace OhMyLib.Models.Configs.Common;

public class BotUserCheckinConfiguration : IEntityTypeConfiguration<BotUserCheckin>
{
    public void Configure(EntityTypeBuilder<BotUserCheckin> builder)
    {
        builder.HasKey(x => x.UserId);

        builder.HasOne(x => x.User)
               .WithOne(x => x.UserCheckin)
               .HasForeignKey<BotUserCheckin>(x => x.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}