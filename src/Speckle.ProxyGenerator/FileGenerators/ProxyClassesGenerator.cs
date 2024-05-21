using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using ProxyInterfaceSourceGenerator.Builders;
using ProxyInterfaceSourceGenerator.Enums;
using ProxyInterfaceSourceGenerator.Extensions;
using ProxyInterfaceSourceGenerator.Models;
using ProxyInterfaceSourceGenerator.Types;
using ProxyInterfaceSourceGenerator.Utils;

namespace ProxyInterfaceSourceGenerator.FileGenerators;

internal partial class ProxyClassesGenerator : BaseGenerator, IFilesGenerator
{
    public ProxyClassesGenerator(Context context, bool supportsNullable) : base(context, supportsNullable)
    {
    }

    public IEnumerable<FileData> GenerateFiles()
    {
        foreach (var ci in Context.Candidates)
        {
            if (TryGenerateFile(ci.Value, out var file))
            {
                yield return file;
            }
        }
    }

    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:Compare symbols correctly", Justification = "<Pending>")]
    private bool TryGenerateFile(ProxyData pd, [NotNullWhen(true)] out FileData? fileData)
    {
        fileData = default;

        if (!TryGetNamedTypeSymbolByFullName(TypeKind.Class, pd.FullMetadataTypeName, pd.Usings, out var targetClassSymbol))
        {
            return false;
        }

        var interfaceName = ResolveInterfaceNameWithOptionalTypeConstraints(targetClassSymbol.Symbol, pd.FullInterfaceName);
        var className = targetClassSymbol.Symbol.ResolveProxyClassName();
        var constructorName = $"{targetClassSymbol.Symbol.Name}Proxy";

        var extendsProxyClasses = GetExtendsProxyData(pd, targetClassSymbol);

        fileData = new FileData(
            $"{targetClassSymbol.Symbol.GetFullMetadataName()}Proxy.g.cs",
            CreateProxyClassCode(pd, targetClassSymbol, extendsProxyClasses, interfaceName, className, constructorName)
        );

        return true;
    }

    private string CreateProxyClassCode(
        ProxyData pd,
        ClassSymbol targetClassSymbol,
        IReadOnlyList<ProxyData> extendsProxyClasses,
        string interfaceName,
        string className,
        string constructorName)
    {
        var firstExtends = extendsProxyClasses.FirstOrDefault();
        var extends = string.Empty;
        var @base = string.Empty;
        var @new = string.Empty;
        var instanceBaseDefinition = string.Empty;
        var instanceBaseSetter = string.Empty;

        if (firstExtends is not null)
        {
            extends = $"global::{firstExtends.NamespaceDot}{firstExtends.ShortMetadataName}Proxy, ";
            @base = " : base(instance)";
            @new = "new ";
            instanceBaseDefinition = $"public {firstExtends.FullQualifiedTypeName} _Instance{firstExtends.FullQualifiedTypeName.GetLastPart()} {{ get; }}";
            instanceBaseSetter = $"_Instance{firstExtends.FullQualifiedTypeName.GetLastPart()} = instance;";
        }

        var @abstract = string.Empty; // targetClassSymbol.Symbol.IsAbstract ? "abstract " : string.Empty;
        var properties = GeneratePublicProperties(targetClassSymbol, pd);
        var methods = GeneratePublicMethods(targetClassSymbol, pd);
        var events = GenerateEvents(targetClassSymbol, pd);
        var operators = GenerateOperators(targetClassSymbol, pd);

        var configurationForMapster = string.Empty;
        if (Context.ReplacedTypes.Count > 0)
        {
            configurationForMapster = GenerateMapperConfigurationForMapster();
        }

        var (namespaceStart, namespaceEnd) = NamespaceBuilder.Build(pd.Namespace);

        var accessibility = pd.Accessibility == ProxyClassAccessibility.Internal ? "internal" : "public";

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
    {accessibility} {@abstract}partial class {className} : {extends}{interfaceName}
    {{
        public {@new}{targetClassSymbol} _Instance {{ get; }}
        {instanceBaseDefinition}
{events +
properties +
methods +
operators}
        public {constructorName}({targetClassSymbol} instance){@base}
        {{
            _Instance = instance;
            {instanceBaseSetter}

{configurationForMapster}
        }}
    }}
{namespaceEnd}
{SupportsNullable.IIf("#nullable restore")}";
    }

    private string GeneratePublicProperties(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();

        foreach (var property in MemberHelper.GetPublicProperties(targetClassSymbol, proxyData))
        {
            var type = GetPropertyType(property, out var isReplaced);

            var instance = !property.IsStatic ?
                "_Instance" :
                $"{targetClassSymbol.Symbol}";

            var propertyName = property.GetSanitizedName();
            var instancePropertyName = $"{instance}.{propertyName}";
            if (property.IsIndexer)
            {
                var parameters = GetMethodParameters(property.Parameters, true);
                propertyName = $"this[{string.Join(", ", parameters)}]";

                var instanceParameters = GetMethodParameters(property.Parameters, false);
                instancePropertyName = $"{instance}[{string.Join(", ", instanceParameters)}]";
            }

            var overrideOrVirtual = string.Empty;
            if (property.IsOverride)
            {
                overrideOrVirtual = "override ";
            }
            else if (property.IsVirtual)
            {
                overrideOrVirtual = "virtual ";
            }

            var getIsPublic = property.GetMethod.IsPublic();
            var setIsPublic = property.SetMethod.IsPublic();

            if (!getIsPublic && !setIsPublic)
            {
                continue;
            }

            string get;
            string set;
            if (isReplaced)
            {
                get = getIsPublic ? $"get => Mapster.TypeAdapter.Adapt<{type}>({instancePropertyName}); " : string.Empty;
                set = setIsPublic ? $"set => {instancePropertyName} = Mapster.TypeAdapter.Adapt<{property.Type}>(value); " : string.Empty;
            }
            else
            {
                get = getIsPublic ? $"get => {instancePropertyName}; " : string.Empty;
                set = setIsPublic ? $"set => {instancePropertyName} = value; " : string.Empty;
            }

            foreach (var attribute in property.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.AppendLine($"        public {overrideOrVirtual}{type} {propertyName} {{ {get}{set}}}");
            str.AppendLine();
        }

        return str.ToString();
    }

    private string GeneratePublicMethods(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();

        var methods = MemberHelper.GetPublicMethods(targetClassSymbol, proxyData);

        foreach (var method in methods)
        {
            var methodParameters = new List<string>();
            var invokeParameters = new List<string>();

            foreach (var parameterSymbol in method.Parameters)
            {
                var type = GetParameterType(parameterSymbol, out _);

                methodParameters.Add(MethodParameterBuilder.Build(parameterSymbol, type));

                // Do not add the '_' for a 'ref' parameter.
                invokeParameters.Add($"{parameterSymbol.GetRefKindPrefix()}{parameterSymbol.GetSanitizedName()}{(!parameterSymbol.IsRef()).IIf("_")}");
            }

            string overrideOrVirtual = string.Empty;
            if (method.IsOverride && method.OverriddenMethod != null)
            {
                var baseType = method.OverriddenMethod.ContainingType.GetFullType();
                if (TryGetNamedTypeSymbolByFullName(TypeKind.Class, baseType, Enumerable.Empty<string>(), out _))
                {
                    overrideOrVirtual = "override ";
                }
            }
            else if (method.IsVirtual)
            {
                overrideOrVirtual = "virtual ";
            }

            string returnTypeAsString = GetReplacedTypeAsString(method.ReturnType, out var returnIsReplaced);

            var whereStatement = GetWhereStatementFromMethod(method);

            foreach (var attribute in method.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.AppendLine($"        public {overrideOrVirtual}{returnTypeAsString} {method.GetMethodNameWithOptionalTypeParameters()}({string.Join(", ", methodParameters)}){whereStatement}");
            str.AppendLine(@"        {");

            foreach (var ps in method.Parameters.Where(p => !p.IsRef()))
            {
                var type = FixType(ps.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), ps.Type.NullableAnnotation);
                string normalOrMap = $" = {ps.GetSanitizedName()}";
                if (ps.RefKind == RefKind.Out)
                {
                    normalOrMap = string.Empty;
                }
                else
                {
                    _ = GetParameterType(ps, out var isReplaced); // TODO : response is not used?
                    if (isReplaced)
                    {
                        normalOrMap = $" = Mapster.TypeAdapter.Adapt<{type}>({ps.GetSanitizedName()})";
                    }
                }

                str.AppendLine($"            {type} {ps.GetSanitizedName()}_{normalOrMap};");
            }

            var methodName = method.GetMethodNameWithOptionalTypeParameters();
            var alternateReturnVariableName = $"result_{methodName.GetDeterministicHashCodeAsString()}";

            string instance = method.IsStatic ? targetClassSymbol.Symbol.ToFullyQualifiedDisplayString() : "_Instance";

            if (returnTypeAsString == "void")
            {
                str.AppendLine($"            {instance}.{methodName}({string.Join(", ", invokeParameters)});");
            }
            else
            {
                str.AppendLine($"            var {alternateReturnVariableName} = {instance}.{methodName}({string.Join(", ", invokeParameters)});");
            }

            foreach (var ps in method.Parameters.Where(p => p.RefKind == RefKind.Out))
            {
                string normalOrMap = $" = {ps.GetSanitizedName()}_";
                if (ps.GetTypeEnum() == TypeEnum.Complex)
                {
                    var type = GetParameterType(ps, out var isReplaced);
                    if (isReplaced)
                    {
                        normalOrMap = $" = Mapster.TypeAdapter.Adapt<{type}>({ps.GetSanitizedName()}_)";
                    }
                }

                str.AppendLine($"            {ps.GetSanitizedName()}{normalOrMap};");
            }

            if (returnTypeAsString != "void")
            {
                if (returnIsReplaced)
                {
                    str.AppendLine($"            return Mapster.TypeAdapter.Adapt<{returnTypeAsString}>({alternateReturnVariableName});");
                }
                else
                {
                    str.AppendLine($"            return {alternateReturnVariableName};");
                }
            }

            str.AppendLine("        }");
            str.AppendLine();
        }

        return str.ToString();
    }

    private string GenerateEvents(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();
        foreach (var @event in MemberHelper.GetPublicEvents(targetClassSymbol, proxyData))
        {
            var name = @event.Key.GetSanitizedName();
            var ps = @event.First().Parameters.First();
            var type = ps.GetTypeEnum() == TypeEnum.Complex ? GetParameterType(ps, out _) : ps.Type.ToString();

            foreach (var attribute in ps.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            str.Append($"        public event {type} {name} {{");

            if (@event.Any(e => e.MethodKind == MethodKind.EventAdd))
            {
                str.Append($" add {{ _Instance.{name} += value; }}");
            }
            if (@event.Any(e => e.MethodKind == MethodKind.EventRemove))
            {
                str.Append($" remove {{ _Instance.{name} -= value; }}");
            }

            str.AppendLine(" }");
            str.AppendLine();
        }

        return str.ToString();
    }

    private string GenerateOperators(ClassSymbol targetClassSymbol, ProxyData proxyData)
    {
        var str = new StringBuilder();
        foreach (var @operator in MemberHelper.GetPublicStaticOperators(targetClassSymbol, proxyData))
        {
            foreach (var attribute in @operator.GetAttributesAsList())
            {
                str.AppendLine($"        {attribute}");
            }

            if (!@operator.Parameters.Any())
            {
                continue;
            }

            var parameter = @operator.Parameters.First();
            var proxyClassName = targetClassSymbol.Symbol.ResolveProxyClassName();

            var operatorType = @operator.Name.ToLowerInvariant().Replace("op_", string.Empty);
            if (operatorType == "explicit")
            {
                var returnTypeAsString = GetReplacedTypeAsString(@operator.ReturnType, out _);

                str.AppendLine($"        public static explicit operator {returnTypeAsString}({proxyClassName} {parameter.Name})");
                str.AppendLine(@"        {");
                str.AppendLine($"            return ({returnTypeAsString}) {parameter.Name}._Instance;");
                str.AppendLine(@"        }");
            }
            else
            {
                var returnTypeAsString = GetReplacedTypeAsString(parameter.Type, out _);

                str.AppendLine($"        public static implicit operator {proxyClassName}({returnTypeAsString} {parameter.Name})");
                str.AppendLine(@"        {");
                str.AppendLine($"            return new {proxyClassName}(({targetClassSymbol.Symbol.Name}) {parameter.Name});");
                str.AppendLine(@"        }");
            }

            str.AppendLine();
        }

        return str.ToString();
    }
}