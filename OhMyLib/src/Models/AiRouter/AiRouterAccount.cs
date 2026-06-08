using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OhMyLib.Models.Common;

namespace OhMyLib.Models.AiRouter;

[Table("ai_router_accounts")]
public class AiRouterAccount
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(Order = 0)]
    public long Id { get; set; }

    [Column(Order = 1)] public long OwnerUserId { get; set; }

    [Column(Order = 2)] public virtual BotUser OwnerBotUser { get; set; } = null!;

    [Column(Order = 3), StringLength(255)] public string Account { get; set; } = string.Empty;

    [Column(Order = 4), StringLength(512)] public string Password { get; set; } = string.Empty;

    [Column(Order = 5)] public bool AutoSignEnabled { get; set; }

    public DateTimeOffset CreateAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdateAt { get; set; } = DateTimeOffset.UtcNow;
}
