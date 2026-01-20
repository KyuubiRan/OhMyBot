using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Models.Common;

namespace OhMyLib.Models.Kuro;

[Table("kuro_users")]
public class KuroUser
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(Order = 0)]
    public long Id { get; set; }

    [Column(Order = 1)] public long OwnerUserId { get; set; }

    [Column(Order = 2)] public virtual BotUser OwnerBotUser { get; init; } = null!;

    [Column(Order = 3)] public RegionType Region { get; set; }

    public KuroBbsTaskType BbsTask { get; set; }

    public virtual List<KuroGameConfig> GameConfigs { get; set; } = [];

    [StringLength(1024)] public string Token { get; set; } = string.Empty;

    public DateTimeOffset CreateAt { get; set; } = DateTimeOffset.UtcNow;
}