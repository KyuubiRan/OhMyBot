using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OhMyLib.Enums;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Models.Common;

[Table("users")]
public class BotUser
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(Order = 0)]
    public long Id { get; set; }

    [Column(Order = 1), StringLength(64)] public string OwnerId { get; set; } = string.Empty;

    [Column(Order = 2)] public SoftwareType OwnerType { get; set; }

    [Column(Order = 3)] public UserPrivilege Privilege { get; set; } = UserPrivilege.None;

    public DateTimeOffset CreateAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual KuroUser? KuroUser { get; set; } = null;

    public virtual BotUserCheckin? UserCheckin { get; set; }

    public int Coin { get; set; }
}

public static class UserExtensions
{
    extension(BotUser botUser)
    {
        public bool IsUser => botUser.Privilege >= UserPrivilege.User;
        public bool IsAdmin => botUser.Privilege >= UserPrivilege.Admin;
        public bool IsSuperAdmin => botUser.Privilege >= UserPrivilege.SuperAdmin;
        public bool IsOwner => botUser.Privilege == UserPrivilege.Owner;
    }
}