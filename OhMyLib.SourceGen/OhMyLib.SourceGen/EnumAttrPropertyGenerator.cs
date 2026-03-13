using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OhMyLib.SourceGen;

[Generator]
public class EnumAttrPropertyGenerator : IIncrementalGenerator
{
    private const string AutoGenerateAttributeName = "AutoGenerateEnumAttrPropertyAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 找到所有标记了 AutoGenerateEnumAttrProperty 的 Attribute 类
        var attributeDeclarations = context.SyntaxProvider
                                           .CreateSyntaxProvider(
                                               predicate: static (node, _) => IsCandidateAttributeClass(node),
                                               transform: static (ctx, _) => GetAttributeClassInfo(ctx))
                                           .Where(static info => info != null);

        var compilationAndAttributes = context.CompilationProvider
                                              .Combine(attributeDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndAttributes, GenerateSource);
    }

    private static bool IsCandidateAttributeClass(SyntaxNode node)
    {
        // 查找类声明，且类名以 Attribute 结尾
        if (node is ClassDeclarationSyntax classDeclaration)
        {
            return classDeclaration.Identifier.Text.EndsWith("Attribute");
        }

        return false;
    }

    private static AttributeClassInfo? GetAttributeClassInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
            return null;

        // 检查是否标记了 AutoGenerateEnumAttrPropertyAttribute
        var autoGenAttr = classSymbol.GetAttributes()
                                     .FirstOrDefault(attr =>
                                                         attr.AttributeClass != null &&
                                                         attr.AttributeClass.Name == AutoGenerateAttributeName);

        if (autoGenAttr == null)
            return null;

        // 获取 TargetEnumType
        INamedTypeSymbol? targetEnumType = null;

        // 检查命名参数
        foreach (var namedArg in autoGenAttr.NamedArguments)
        {
            if (namedArg.Key == "TargetEnumType" && namedArg.Value.Value is INamedTypeSymbol enumType)
            {
                targetEnumType = enumType;
                break;
            }
        }

        if (targetEnumType == null || targetEnumType.TypeKind != TypeKind.Enum)
            return null;

        // 获取 Attribute 类的所有 public 属性
        var properties = new List<PropertyInfo>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IPropertySymbol propertySymbol &&
                propertySymbol.DeclaredAccessibility == Accessibility.Public &&
                !propertySymbol.IsStatic &&
                propertySymbol.GetMethod != null)
            {
                properties.Add(new PropertyInfo(
                                   propertySymbol.Name,
                                   propertySymbol.Type.ToDisplayString()));
            }
        }

        if (properties.Count == 0)
            return null;

        return new AttributeClassInfo(
            classSymbol.ToDisplayString(),
            classSymbol.Name,
            targetEnumType.ToDisplayString(),
            targetEnumType.Name,
            GetEnumNamespace(targetEnumType) ?? "",
            properties);
    }

    private static string? GetEnumNamespace(INamedTypeSymbol enumSymbol)
    {
        var ns = enumSymbol.ContainingNamespace;
        if (ns == null || ns.IsGlobalNamespace)
            return null;
        return ns.ToDisplayString();
    }

    private static void GenerateSource(
        SourceProductionContext context,
        (Compilation Compilation, ImmutableArray<AttributeClassInfo?> Attributes) source)
    {
        var compilation = source.Compilation;
        var attributeInfos = source.Attributes
                                   .Where(a => a != null)
                                   .Distinct(new AttributeClassInfoComparer()!)
                                   .ToList();

        foreach (var attrInfo in attributeInfos)
        {
            if (attrInfo == null)
                continue;

            var enumSymbol = compilation.GetTypeByMetadataName(attrInfo.TargetEnumFullName);
            if (enumSymbol == null)
                continue;

            var enumMembers = new List<EnumMemberInfo>();

            foreach (var member in enumSymbol.GetMembers())
            {
                if (member is IFieldSymbol { HasConstantValue: true } fieldSymbol)
                {
                    // 查找该字段上的 Attribute
                    var fieldAttr = fieldSymbol.GetAttributes()
                                               .FirstOrDefault(attr =>
                                                                   attr.AttributeClass != null &&
                                                                   attr.AttributeClass.ToDisplayString() == attrInfo.AttributeFullName);

                    if (fieldAttr == null)
                        continue;

                    var propertyValues = new Dictionary<string, object>();

                    foreach (var namedArg in fieldAttr.NamedArguments)
                    {
                        var v = namedArg.Value.Value;
                        if (v == null)
                            continue;
                        propertyValues[namedArg.Key] = v;
                    }

                    enumMembers.Add(new EnumMemberInfo(fieldSymbol.Name, propertyValues));
                }
            }

            if (enumMembers.Count == 0)
                continue;

            var sourceCode = GenerateExtensionCode(attrInfo, enumMembers);
            var fileName = attrInfo.TargetEnumName + "Extensions.g.cs";

            context.AddSource(fileName, SourceText.From(sourceCode, Encoding.UTF8));
        }
    }

    private static string GenerateExtensionCode(AttributeClassInfo attrInfo, List<EnumMemberInfo> enumMembers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(attrInfo.TargetEnumNamespace))
        {
            sb.AppendLine("namespace " + attrInfo.TargetEnumNamespace);
            sb.AppendLine("{");
        }

        sb.AppendLine("    public static class " + attrInfo.TargetEnumName + "Extensions");
        sb.AppendLine("    {");
        sb.AppendLine("        extension(" + attrInfo.TargetEnumName + " " + ToCamelCase(attrInfo.TargetEnumName) + ")");
        sb.AppendLine("        {");

        foreach (var property in attrInfo.Properties)
        {
            sb.AppendLine("            public " + property.TypeName + " " + property.Name + " => " + ToCamelCase(attrInfo.TargetEnumName) + " switch");
            sb.AppendLine("            {");

            foreach (var member in enumMembers)
            {
                if (member.PropertyValues.TryGetValue(property.Name, out var value))
                {
                    var valueStr = FormatValue(value);
                    sb.AppendLine("                " + attrInfo.TargetEnumName + "." + member.Name + " => " + valueStr + ",");
                }
            }

            sb.AppendLine("                _ => throw new ArgumentOutOfRangeException(nameof(" + ToCamelCase(attrInfo.TargetEnumName) + "), " +
                          ToCamelCase(attrInfo.TargetEnumName) + ", null)");
            sb.AppendLine("            };");
            sb.AppendLine();
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");

        if (!string.IsNullOrEmpty(attrInfo.TargetEnumNamespace))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return "null";

        if (value is string strValue)
            return "\"" + strValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        if (value is bool boolValue)
            return boolValue ? "true" : "false";

        if (value is char charValue)
            return "'" + charValue + "'";

        return value.ToString() ?? "";
    }
}

internal class AttributeClassInfo(
    string attributeFullName,
    string attributeName,
    string targetEnumFullName,
    string targetEnumName,
    string targetEnumNamespace,
    List<PropertyInfo> properties)
{
    public string AttributeFullName { get; } = attributeFullName;
    public string AttributeName { get; } = attributeName;
    public string TargetEnumFullName { get; } = targetEnumFullName;
    public string TargetEnumName { get; } = targetEnumName;
    public string TargetEnumNamespace { get; } = targetEnumNamespace;
    public List<PropertyInfo> Properties { get; } = properties;
}

internal class PropertyInfo(string name, string typeName)
{
    public string Name { get; } = name;
    public string TypeName { get; } = typeName;
}

internal class EnumMemberInfo(string name, Dictionary<string, object> propertyValues)
{
    public string Name { get; } = name;
    public Dictionary<string, object> PropertyValues { get; } = propertyValues;
}

internal class AttributeClassInfoComparer : IEqualityComparer<AttributeClassInfo>
{
    public bool Equals(AttributeClassInfo? x, AttributeClassInfo? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        return x.AttributeFullName == y.AttributeFullName &&
               x.TargetEnumFullName == y.TargetEnumFullName;
    }

    public int GetHashCode(AttributeClassInfo? obj)
    {
        if (obj == null) return 0;
        unchecked
        {
            var hash = 17;
            hash = hash * 23 + obj.AttributeFullName.GetHashCode();
            hash = hash * 23 + obj.TargetEnumFullName.GetHashCode();
            return hash;
        }
    }
}