using System.Text;
using Speckle.ProxyGenerator.Extensions;
using Speckle.ProxyGenerator.Models;

namespace Speckle.ProxyGenerator.FileGenerators;

internal record ProxyMapItem(string BaseType, string InterfaceType, string ProxyType);

internal class ExtraFilesGenerator : IFileGenerator
{
    private const string Name = "Speckle.ProxyGenerator.Extra.g.cs";

    public FileData GenerateFile(List<ProxyMapItem> proxyMapItems, bool supportsNullable)
    {
        var sb = new StringBuilder();
        foreach (var item in proxyMapItems)
        {
            sb.AppendLine(
                $"Add<{item.BaseType}, {item.InterfaceType}, {item.ProxyType}>(x => new {item.ProxyType}(x));"
            );
        }
        return new FileData(
            $"{Name}",
            $@"//----------------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by https://github.com/specklesystems/ProxyGenerator
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//----------------------------------------------------------------------------------------

{supportsNullable.IIf("#nullable enable")}
using System;

namespace Speckle.ProxyGenerator
{{
    [AttributeUsage(AttributeTargets.Interface)]
    internal sealed class ProxyAttribute : Attribute
    {{
        public Type Type {{ get; }}
        public ImplementationOptions Options {{ get; }}
        public ProxyClassAccessibility Accessibility {{ get; }}
        public string[]? MembersToIgnore {{ get; }}

        public ProxyAttribute(Type type) : this(type, ImplementationOptions.None, ProxyClassAccessibility.Public)
        {{
        }}

        public ProxyAttribute(Type type, ImplementationOptions options) : this(type, options, ProxyClassAccessibility.Public)
        {{
        }}

       	public ProxyAttribute(Type type, ProxyClassAccessibility accessibility) : this(type, ImplementationOptions.None, accessibility)
        {{
        }}

        public ProxyAttribute(Type type, ImplementationOptions options, ProxyClassAccessibility accessibility) : this(type, options, accessibility, null)
        {{
        }}

        public ProxyAttribute(Type type, string[]? membersToIgnore) : this(type, ImplementationOptions.None, ProxyClassAccessibility.Public, null)
        {{
        }}

        public ProxyAttribute(Type type, ImplementationOptions options, string[]? membersToIgnore) : this(type, options, ProxyClassAccessibility.Public, null)
        {{
        }}

        public ProxyAttribute(Type type, ImplementationOptions options, ProxyClassAccessibility accessibility, string[]? membersToIgnore)
        {{
            Type = type;
            Options = options;
            Accessibility = accessibility;
            MembersToIgnore = membersToIgnore;
        }}
    }}

    [Flags]
    internal enum ProxyClassAccessibility
    {{
        Public = 0,

        Internal = 1
    }}
    [Flags]
    internal enum ImplementationOptions
    {{
        None = 0,

        ProxyBaseClasses = 1,

        ProxyInterfaces = 2,

        UseExtendedInterfaces = 4,

        ProxyForBaseInterface = 8
    }}

    public static class ProxyMap
    {{
      private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<Type, Type> s_revitToInterfaceMap = new();
      private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<Type, Type> s_proxyToInterfaceMap = new();
      private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<Type, Type> s_interfaceToRevit = new();
      private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object>> s_proxyFactory = new();

      static ProxyMap()
      {{
        {sb}
      }}

      private static void Add<T, TInterface, TProxy>(Func<T, TProxy> f)
        where TInterface : notnull
        where TProxy : TInterface
      {{
        s_revitToInterfaceMap.TryAdd(typeof(T), typeof(TInterface));
        s_proxyToInterfaceMap.TryAdd(typeof(TProxy), typeof(TInterface));
        s_proxyFactory.TryAdd(typeof(TInterface), w => f((T)w));
        s_interfaceToRevit.TryAdd(typeof(TInterface), typeof(T));
      }}

      public static Type? GetMappedTypeFromHostType(Type type)
      {{
        if (s_revitToInterfaceMap.TryGetValue(type, out var t))
        {{
          return t;
        }}
        return null;
      }}

      public static Type? GetMappedTypeFromProxyType(Type type)
      {{
        if (s_proxyToInterfaceMap.TryGetValue(type, out var t))
        {{
          return t;
        }}

        return null;
      }}

      public static Type? GetHostTypeFromMappedType(Type type)
      {{
        if (s_interfaceToRevit.TryGetValue(type, out var t))
        {{
          return t;
        }}

        return null;
      }}

      public static object CreateProxy(Type type, object toWrap) => s_proxyFactory[type](toWrap);
      public static T CreateProxy<T>(object toWrap) => (T)CreateProxy(typeof(T), toWrap);
    }}
    public static class MapsterAdapter
    {{
        public static TDestination? AdaptNull<TDestination>(object? source)
        {{
            if (source is null)
            {{
                return default;
            }}

            return Mapster.TypeAdapter.Adapt<TDestination>(source);
        }}
    }}
{supportsNullable.IIf("#nullable restore")}
}}"
        );
    }
}
