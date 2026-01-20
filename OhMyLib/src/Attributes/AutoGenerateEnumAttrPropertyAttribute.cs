namespace OhMyLib.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AutoGenerateEnumAttrPropertyAttribute : Attribute
{
    public required Type TargetEnumType { get; init; }
}