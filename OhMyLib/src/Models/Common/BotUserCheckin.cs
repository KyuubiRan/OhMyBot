using System.ComponentModel.DataAnnotations.Schema;

namespace OhMyLib.Models.Common;

[Table("user_checkins")]
public class BotUserCheckin
{
    public long UserId { get; set; }
    public int TotalDays { get; set; }
    public int StreakDays { get; set; }
    public int MaxStreakDays { get; set; }
    public DateTimeOffset? LastCheckinTime { get; set; }

    public virtual BotUser User { get; set; } = null!;
}