using OhMyLib.Enums.Kuro;

namespace OhMyLib.Attributes.Kuro;

[AttributeUsage(AttributeTargets.Field)]
[AutoGenerateEnumAttrProperty(TargetEnumType = typeof(KuroGameTaskType))]
public class KuroGameTaskAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
}