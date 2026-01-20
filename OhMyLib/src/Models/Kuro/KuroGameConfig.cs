using System.ComponentModel.DataAnnotations.Schema;
using OhMyLib.Enums.Kuro;

namespace OhMyLib.Models.Kuro;

[Table("kuro_game_configs")]
public class KuroGameConfig
{
    [Column(Order = 0)] public long Id { get; set; }

    [Column(Order = 1)] public long KuroUserId { get; set; }

    [Column(Order = 2)] public virtual KuroUser KuroUser { get; set; } = null!;

    [Column(Order = 3)] public KuroGameType GameType { get; set; }

    [Column(Order = 4)] public long GameCharacterUid { get; set; }

    [Column(Order = 5)] public KuroGameTaskType TaskType { get; set; } = KuroGameTaskType.None;
}