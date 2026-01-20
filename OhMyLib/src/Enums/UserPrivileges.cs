namespace OhMyLib.Enums;

public enum UserPrivilege : byte
{
    None = 0,

    User = 1,

    Admin = 10,

    SuperAdmin = 100,

    Owner = byte.MaxValue
}