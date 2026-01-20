using OhMyLib.Enums.Kuro;

namespace OhMyLib.Attributes.Kuro;

[AttributeUsage(AttributeTargets.Field)]
[AutoGenerateEnumAttrProperty(TargetEnumType = typeof(KuroGameType))]
public sealed class KuroGameAttrAttribute : Attribute
{
    public required string Name { get; init; }
    public required string ServerId { get; init; }
}