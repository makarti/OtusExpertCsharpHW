using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Parser.SourceGenerators;

[Generator]
public sealed class BinarySerializerGenerator : IIncrementalGenerator
{

    private const string AttributeFullName = "Parser.SourceGenerators.GenerateBinarySerializerAttribute";

    private const string AttributeSource = """
        namespace Parser.SourceGenerators
        {
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            public sealed class GenerateBinarySerializerAttribute : System.Attribute
            {
            }
        }
        """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("GenerateBinarySerializerAttribute.g.cs", AttributeSource));

        var classDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        context.RegisterSourceOutput(classDeclarations, static (spc, classes) =>
        {
            foreach (var classSymbol in classes.Distinct<INamedTypeSymbol>((IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default))
            {
                var source = GenerateSerializer(classSymbol);
                if (source is not null)
                {
                    spc.AddSource($"{classSymbol.Name}.BinarySerializer.g.cs", source);
                }
            }
        });
    }

    private static string? GenerateSerializer(INamedTypeSymbol classSymbol)
    {
        var properties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public
                        && !p.IsStatic
                        && p.GetMethod is not null
                        && p.SetMethod is not null)
            .ToList();

        if (properties.Count == 0)
        {
            return null;
        }

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine();

        if (namespaceName is not null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"partial class {classSymbol.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    public void SerializeToBinary(Stream stream)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);");

        foreach (var property in properties)
        {
            var writeStatement = GetWriteStatement(property);
            if (writeStatement is not null)
            {
                sb.AppendLine($"        {writeStatement}");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static {classSymbol.Name} DeserializeFromBinary(Stream stream)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);");
        sb.AppendLine($"        return new {classSymbol.Name}");
        sb.AppendLine("        {");

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var readExpression = GetReadExpression(property);
            if (readExpression is not null)
            {
                var comma = i < properties.Count - 1 ? "," : string.Empty;
                sb.AppendLine($"            {property.Name} = {readExpression}{comma}");
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? GetWriteStatement(IPropertySymbol property)
    {
        var typeName = property.Type.ToDisplayString();
        var accessor = $"{property.Name}";

        return typeName switch
        {
            "string" => $"writer.Write({accessor} ?? string.Empty);",
            "int" => $"writer.Write({accessor});",
            "bool" => $"writer.Write({accessor});",
            "DateTime" => $"writer.Write({accessor}.Ticks);",
            _ => null
        };
    }

    private static string? GetReadExpression(IPropertySymbol property)
    {
        var typeName = property.Type.ToDisplayString();

        return typeName switch
        {
            "string" => "reader.ReadString()",
            "int" => "reader.ReadInt32()",
            "bool" => "reader.ReadBoolean()",
            "DateTime" => "new DateTime(reader.ReadInt64())",
            _ => null
        };
    }
}
