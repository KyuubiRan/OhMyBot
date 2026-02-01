using OhMyLib.Enums.Kuro;

namespace OhMyLib.Attributes.Kuro;

[AttributeUsage(AttributeTargets.Field)]
[AutoGenerateEnumAttrProperty(TargetEnumType = typeof(KuroBbsTaskType))]
public class KuroBbsTaskAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
}