using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProxyInterfaceSourceGenerator.Enums;
using ProxyInterfaceSourceGenerator.Extensions;
using ProxyInterfaceSourceGenerator.Models;
using ProxyInterfaceSourceGenerator.Utils;

namespace ProxyInterfaceSourceGenerator.FileGenerators;

internal class PartialInterfacesGenerator : BaseGenerator, IFilesGenerator
{
    private IReadOnlyCollection<INamedTypeSymbol> _implementedInterfaces = new List<INamedTypeSymbol>();

    public PartialInterfacesGenerator(Context context, bool supportsNullable) : base(context, supportsNullable)
    {
    }

    public IEnumerable<FileData> GenerateFiles()
    {
        foreach (var ci in Context.Candidates)
        {
            if (TryGenerateFile(ci.Key, ci.Value, out var file))
            {
                yield return file;
            }
        }
    }

    private bool TryGenerateFile(InterfaceDeclarationSyntax ci, ProxyData pd, [NotNullWhen(true)] out FileData? fileData)
    {
        fileData = default;

        if (!TryGetNamedTypeSymbolByFullName(TypeKind.Interface, ci.Identifier.ToString(), pd.Usings, out var sourceInterfaceSymbol))
        {
            return false;
        }

        if (!TryGetNamedTypeSymbolByFullName(TypeKind.Class, pd.FullMetadataTypeName, pd.Usings, out var targetClassSymbol))
        {
            return false;
        }

        var interfaceName = ResolveInterfaceNameWithOptionalTypeConstraints(targetClassSymbol.Symbol, pd.ShortInterfaceName);

        fileData = new FileData(
            $"{sourceInterfaceSymbol.Symbol.GetFullMetadataName()}.g.cs",
            CreatePartialInterfaceCode(pd.Namespace, targetClassSymbol, interfaceName, pd)
        );

        return true;
    }

    private string CreatePartialInterfaceCode(
        string ns,
        ClassSymbol classSymbol,
        string interfaceName,
        ProxyData proxyData)
    {
        var extendsProxyClasses = GetExtendsProxyData(proxyData, classSymbol);
        _implementedInterfaces = classSymbol.Symbol.ResolveImplementedInterfaces(proxyData.ProxyBaseClasses);
        var implementedInterfacesNames = _implementedInterfaces.Select(i => i.ToFullyQualifiedDisplayString()).ToArray();
        var implements = implementedInterfacesNames.Any() ? $" : {string.Join(", ", implementedInterfacesNames)}" : string.Empty;
        var @new = extendsProxyClasses.Any() ? "new " : string.Empty;
        var (namespaceStart, namespaceEnd) = NamespaceBuilder.Build(ns);
        var events = GenerateEvents(classSymbol, proxyData);
        var properties = GenerateProperties(classSymbol, proxyData);
        var methods = GenerateMethods(classSymbol, proxyData).TrimEnd();

        return $@"//----------------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by https://github.com/StefH/ProxyInterfaceSourceGenerator.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//----------------------------------------------------------------------------------------

{SupportsNullable.IIf("#nullable enable")}
using System;

{namespaceStart}
    public partial interface {interfaceName}{implements}
    {{
        {@new}{classSymbol} _Instance {{ get; }}

{events +
properties +
methods}
    }}
{namespaceEnd}
{SupportsNullable.IIf("#nullable restore")}";
    }

    private Func<T, bool> InterfaceFilter<T>() where T : ISymbol
    {
        var hashSet = new HashSet<string>();
        foreach (var @interface in _implementedInterfaces)
        {
            var members = @interface.AllInterfaces.Aggregate(@interface.GetMembers(), (xs, x) => xs.AddRange(x.GetMembers()));
            foreach (var member in members)
            {
                hashSet.Add(member.Name);
            }
        }

        // Member is not already implemented in another interface.
        return t => !hashSet.Contains(t.Name);
    }

    private string GenerateProperties(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();

        foreach (var property in MemberHelper.GetPublicProperties(targetClassSymbol, proxyData, InterfaceFilter<IPropertySymbol>()))
        {
            var type = GetPropertyType(property, out var isReplaced);

            var getterSetter = isReplaced ? property.ToPropertyDetails(type) : property.ToPropertyDetails();
            if (getterSetter is null)
            {
                continue;
            }

            var propertyName = getterSetter.Value.PropertyName;

            if (property.IsIndexer)
            {
                var methodParameters = GetMethodParameters(property.Parameters, true);
                propertyName = $"this[{string.Join(", ", methodParameters)}]";
            }

            foreach (var attribute in property.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.AppendLine($"        {getterSetter.Value.PropertyType} {propertyName} {getterSetter.Value.GetSet}");
            str.AppendLine();
        }
        return str.ToString();
    }

    private string GenerateMethods(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();
        foreach (var method in MemberHelper.GetPublicMethods(targetClassSymbol, proxyData, InterfaceFilter<IMethodSymbol>()))
        {
            var methodParameters = GetMethodParameters(method.Parameters, true);
            var whereStatement = GetWhereStatementFromMethod(method);

            foreach (var attribute in method.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.AppendLine($"        {GetReplacedTypeAsString(method.ReturnType, out _)} {method.GetMethodNameWithOptionalTypeParameters()}({string.Join(", ", methodParameters)}){whereStatement};");
            str.AppendLine();
        }

        return str.ToString();
    }

    private string GenerateEvents(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();
        foreach (var @event in MemberHelper.GetPublicEvents(targetClassSymbol, proxyData, InterfaceFilter<IMethodSymbol>()))
        {
            var ps = @event.First().Parameters.First();
            var type = ps.GetTypeEnum() == TypeEnum.Complex ? GetParameterType(ps, out _) : ps.Type.ToString();

            foreach (var attribute in ps.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.AppendLine($"        event {type} {@event.Key.GetSanitizedName()};");
            str.AppendLine();
        }

        return str.ToString();
    }
}