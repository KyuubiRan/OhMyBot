using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;

namespace OhMyLib.Models.Kuro;

[Table("kuro_users")]
public class KuroUser
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(Order = 0)]
    public long Id { get; set; }

    [Column(Order = 1)]
    [StringLength(255)]
    public string OwnerId { get; set; } = string.Empty;

    [Column(Order = 2)] public OwnerIdType OwnerType { get; set; }

    [Column(Order = 3)] public RegionType Region { get; set; }

    public KuroBbsTaskType BbsTask { get; set; }

    public virtual List<KuroGameConfig> GameConfigs { get; set; } = [];

    [StringLength(1024)] public string Token { get; set; } = string.Empty;
}