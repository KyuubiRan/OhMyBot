namespace OhMyLib.Enums;

[Flags]
public enum ChatType : byte
{
    Private = 1 << 0,
    Group = 1 << 1,
    
    All = Private | Group
}