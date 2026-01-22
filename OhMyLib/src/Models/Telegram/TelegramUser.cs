using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OhMyLib.Models.Telegram;

[Table("telegram_users")]
public class TelegramUser
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(Order = 0)]
    public long Id { get; set; }

    public long UserId { get; set; }

    [StringLength(255)] public string? Username { get; set; } = null;

    [StringLength(255)] public string? FirstName { get; set; } = null;

    [StringLength(255)] public string? LastName { get; set; } = null;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}