// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

public partial class StateSourceGenerator
{
    /// <summary>
    /// Determines if a type is mutable (can have its contents changed).
    /// Mutable types: List, Dictionary, Array, State classes, and any type containing them.
    /// Immutable: primitives, string, enum, value tuples with immutable elements.
    /// 
    /// This is a superset of TypeKindHandler.HasStateClass — it includes collections
    /// (which are mutable even without State classes) in addition to State classes.
    /// </summary>
    private static bool IsMutableType(TypeInfo typeInfo)
    {
        switch (typeInfo.Kind)
        {
            case TypeKind.List:
            case TypeKind.Dictionary:
            case TypeKind.Array:
                return true;

            case TypeKind.StateClass:
                return true;

            case TypeKind.Nullable:
                return typeInfo.InnerType != null && IsMutableType(typeInfo.InnerType);

            case TypeKind.ValueTuple:
                return typeInfo.TupleElements?.Any(IsMutableType) ?? false;

            case TypeKind.Unknown:
            case TypeKind.UnsupportedInterface:
            case TypeKind.UnsupportedGeneric:
                // Already emit STATE003 for these, so don't double-error
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Classifies a type symbol into a TypeInfo for code generation.
    /// 
    /// ORDERING CONTRACT: The checks in this method are order-dependent.
    /// Each check must come before later ones for correctness:
    ///   1. Generic type parameters — must be first; they're not INamedTypeSymbol
    ///   2. Interface collection types — must come before concrete collections
    ///   3. Nullable value types — MUST come before primitives, because Nullable{T}
    ///      is itself a value type and would match ClassifyWellKnownType
    ///   4. Primitives/well-known types — after nullable to avoid misclassifying int?
    ///   5. String — after primitives (string is not a WellKnownType)
    ///   6. Enum — after primitives
    ///   7. Dictionary, List, Array — concrete collections (order among these doesn't matter)
    ///   8. State classes — after collections to avoid treating List{State} as State
    ///   9. Value tuples — after everything else that might be INamedTypeSymbol
    ///  10. Fallback — unknown/unsupported type
    /// </summary>
    private static (TypeInfo, List<DiagnosticInfo>) AnalyzeType(ITypeSymbol typeSymbol, string propertyName, Location? location)
    {
        var diagnostics = new List<DiagnosticInfo>();

        // 1. Check for generic type parameter
        if (typeSymbol is ITypeParameterSymbol typeParam)
        {
            // Check if constrained to State
            foreach (var constraint in typeParam.ConstraintTypes)
            {
                if (IsStateClass(constraint))
                {
                    return (new TypeInfo(TypeKind.StateClass, typeSymbol.Name), diagnostics);
                }
            }
            // No State constraint - emit error
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticKind.UnconstrainedGenericType,
                propertyName,
                typeSymbol.Name,
                location));
            return (new TypeInfo(TypeKind.UnsupportedGeneric, typeSymbol.Name), diagnostics);
        }

        // 2. Check for interface collection types (IList, IDictionary, etc.)
        if (IsInterfaceCollectionType(typeSymbol))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticKind.InterfaceCollectionType,
                propertyName,
                typeSymbol.ToDisplayString(),
                location));
            return (new TypeInfo(TypeKind.UnsupportedInterface, typeSymbol.ToDisplayString()), diagnostics);
        }

        // 3. Check for nullable value type — MUST come before primitives
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var (innerType, innerDiags) = AnalyzeType(namedType.TypeArguments[0], propertyName, location);
            diagnostics.AddRange(innerDiags);
            return (new TypeInfo(TypeKind.Nullable, typeSymbol.ToDisplayString(), innerType), diagnostics);
        }

        // 4. Check for primitives and well-known types
        var wellKnownType = ClassifyWellKnownType(typeSymbol);
        if (wellKnownType != WellKnownType.None)
        {
            return (new TypeInfo(TypeKind.Primitive, typeSymbol.ToDisplayString(), wellKnownType: wellKnownType), diagnostics);
        }

        // 5. Check for string
        if (typeSymbol.SpecialType == SpecialType.System_String)
        {
            return (new TypeInfo(TypeKind.String, "string"), diagnostics);
        }

        // 6. Check for enum
        if (typeSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
        {
            return (new TypeInfo(TypeKind.Enum, typeSymbol.ToDisplayString()), diagnostics);
        }

        // 7a. Check for Dictionary<K, V>
        if (typeSymbol is INamedTypeSymbol dictType && IsDictionaryType(dictType))
        {
            var keyTypeSymbol = dictType.TypeArguments[0];
            var (keyType, keyDiags) = AnalyzeType(keyTypeSymbol, propertyName, location);
            var (valueType, valDiags) = AnalyzeType(dictType.TypeArguments[1], propertyName, location);
            diagnostics.AddRange(keyDiags);
            diagnostics.AddRange(valDiags);

            // Validate dictionary key type - must be primitive, string, enum, or well-known
            if (!IsAllowedDictionaryKeyType(keyTypeSymbol))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.UnsupportedDictionaryKeyType,
                    propertyName,
                    keyTypeSymbol.ToDisplayString(),
                    location));
            }

            return (new TypeInfo(TypeKind.Dictionary, typeSymbol.ToDisplayString(), keyType, valueType), diagnostics);
        }

        // 7b. Check for List<T>
        if (typeSymbol is INamedTypeSymbol listType && IsListType(listType))
        {
            var (elementType, elemDiags) = AnalyzeType(listType.TypeArguments[0], propertyName, location);
            diagnostics.AddRange(elemDiags);
            return (new TypeInfo(TypeKind.List, typeSymbol.ToDisplayString(), elementType), diagnostics);
        }

        // 7c. Check for array
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            // Reject multidimensional arrays
            if (arrayType.Rank > 1)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.MultidimensionalArray,
                    propertyName,
                    typeSymbol.ToDisplayString(),
                    location));
                return (new TypeInfo(TypeKind.Unknown, typeSymbol.ToDisplayString()), diagnostics);
            }

            var (elementType, elemDiags) = AnalyzeType(arrayType.ElementType, propertyName, location);
            diagnostics.AddRange(elemDiags);
            return (new TypeInfo(TypeKind.Array, typeSymbol.ToDisplayString(), elementType), diagnostics);
        }

        // 8. Check for [State] class or class that inherits from State
        if (IsStateClass(typeSymbol))
        {
            // Use shared helper for consistent naming with StateClassInfo.FullTypeName
            return (new TypeInfo(TypeKind.StateClass, GetFullTypeName(typeSymbol)), diagnostics);
        }

        // 9. Check for ValueTuple
        if (typeSymbol is INamedTypeSymbol tupleType && tupleType.IsTupleType)
        {
            var elements = new List<TypeInfo>();
            foreach (var elem in tupleType.TupleElements)
            {
                var (elemType, elemDiags) = AnalyzeType(elem.Type, propertyName, location);
                elements.Add(elemType);
                diagnostics.AddRange(elemDiags);
            }
            return (new TypeInfo(TypeKind.ValueTuple, typeSymbol.ToDisplayString(), elements.ToImmutableArray()), diagnostics);
        }

        // 10. Unknown/unsupported type
        diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticKind.UnsupportedExternalType,
            propertyName,
            typeSymbol.ToDisplayString(),
            location));
        return (new TypeInfo(TypeKind.Unknown, typeSymbol.ToDisplayString()), diagnostics);
    }

    /// <summary>
    /// Checks if an attribute is the Accordant.StateAttribute.
    /// Uses fully-qualified name to avoid matching unrelated StateAttribute classes.
    /// </summary>
    private static bool IsAccordantStateAttribute(AttributeData attr)
    {
        return attr.AttributeClass?.ToDisplayString() == StateAttributeFullName;
    }

    /// <summary>
    /// Gets the fully qualified type name for a type symbol.
    /// This is used consistently for both StateClassInfo.FullTypeName and TypeInfo.TypeName
    /// to ensure error propagation lookups match correctly.
    /// Format: "Namespace.TypeName" or just "TypeName" for global namespace types.
    /// </summary>
    private static string GetFullTypeName(ITypeSymbol typeSymbol)
    {
        // Strip nullable reference type annotation to get the canonical type name.
        // E.g., "ItemState?" → "ItemState" so it matches StateClassInfo.FullTypeName.
        var symbol = typeSymbol.WithNullableAnnotation(NullableAnnotation.None);
        var ns = symbol.ContainingNamespace;
        var namespaceName = (ns == null || ns.IsGlobalNamespace)
            ? null
            : ns.ToDisplayString();
        var minimalName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return namespaceName != null
            ? $"{namespaceName}.{minimalName}"
            : minimalName;
    }

    /// <summary>
    /// Checks if a type is a [State] class, inherits from State, or IS the State base class.
    /// </summary>
    private static bool IsStateClass(ITypeSymbol typeSymbol)
    {
        // Check if this IS the Accordant.State base class itself
        if (typeSymbol.Name == "State" &&
            typeSymbol.ContainingNamespace?.ToDisplayString() == "Microsoft.Accordant")
        {
            return true;
        }

        // Check for [State] attribute
        if (typeSymbol.GetAttributes().Any(IsAccordantStateAttribute))
        {
            return true;
        }

        // Check if it inherits from Accordant.State or a [State] class
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            return InheritsFromState(namedType);
        }

        return false;
    }

    /// <summary>
    /// Checks if a type is an interface collection type (IList, IDictionary, etc.)
    /// Used only for better diagnostics (STATE001) - if a new interface type is missed here,
    /// it will still be rejected with a generic STATE003 "unsupported type" error, so this 
    /// is safe even if new collection interfaces are added to .NET in the future.
    /// </summary>
    private static bool IsInterfaceCollectionType(ITypeSymbol type)
    {
        if (type.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface)
            return false;

        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var metadataName = namedType.OriginalDefinition.MetadataName;
            var ns = namedType.ContainingNamespace?.ToDisplayString();
            if (ns == "System.Collections.Generic")
            {
                // MetadataName includes arity suffix, e.g. "IList`1", "IDictionary`2"
                return metadataName == "IList`1" ||
                       metadataName == "IDictionary`2" ||
                       metadataName == "ICollection`1" ||
                       metadataName == "IEnumerable`1" ||
                       metadataName == "ISet`1" ||
                       metadataName == "IReadOnlyList`1" ||
                       metadataName == "IReadOnlyCollection`1" ||
                       metadataName == "IReadOnlyDictionary`2";
            }
        }

        // Non-generic collection interfaces
        var fullName = type.ToDisplayString();
        return fullName == "System.Collections.IEnumerable" ||
               fullName == "System.Collections.ICollection" ||
               fullName == "System.Collections.IList";
    }

    /// <summary>
    /// Checks if a type is System.Collections.Generic.Dictionary&lt;TKey, TValue&gt;
    /// using metadata name instead of string display format.
    /// </summary>
    private static bool IsDictionaryType(INamedTypeSymbol type)
    {
        if (!type.IsGenericType) return false;
        var original = type.OriginalDefinition;
        return original.MetadataName == "Dictionary`2" &&
               original.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    /// <summary>
    /// Checks if a type is System.Collections.Generic.List&lt;T&gt;
    /// using metadata name instead of string display format.
    /// </summary>
    private static bool IsListType(INamedTypeSymbol type)
    {
        if (!type.IsGenericType) return false;
        var original = type.OriginalDefinition;
        return original.MetadataName == "List`1" &&
               original.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    /// <summary>
    /// Classifies a type symbol into a WellKnownType using SpecialType enum and metadata checks.
    /// Returns WellKnownType.None if the type is not a recognized primitive/well-known type.
    /// </summary>
    private static WellKnownType ClassifyWellKnownType(ITypeSymbol type)
    {
        // Check SpecialType first for built-in primitives (reliable, no string matching)
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return WellKnownType.Boolean;
            case SpecialType.System_Byte: return WellKnownType.Byte;
            case SpecialType.System_SByte: return WellKnownType.SByte;
            case SpecialType.System_Int16: return WellKnownType.Int16;
            case SpecialType.System_UInt16: return WellKnownType.UInt16;
            case SpecialType.System_Int32: return WellKnownType.Int32;
            case SpecialType.System_UInt32: return WellKnownType.UInt32;
            case SpecialType.System_Int64: return WellKnownType.Int64;
            case SpecialType.System_UInt64: return WellKnownType.UInt64;
            case SpecialType.System_Single: return WellKnownType.Single;
            case SpecialType.System_Double: return WellKnownType.Double;
            case SpecialType.System_Decimal: return WellKnownType.Decimal;
            case SpecialType.System_Char: return WellKnownType.Char;
            case SpecialType.System_DateTime: return WellKnownType.DateTime;
        }

        // Check for well-known types not covered by SpecialType, using namespace + name
        if (type is INamedTypeSymbol namedType)
        {
            var ns = namedType.ContainingNamespace?.ToDisplayString();
            if (ns == "System")
            {
                switch (namedType.Name)
                {
                    case "DateTime": return WellKnownType.DateTime;
                    case "DateTimeOffset": return WellKnownType.DateTimeOffset;
                    case "TimeSpan": return WellKnownType.TimeSpan;
                    case "Guid": return WellKnownType.Guid;
                    case "DateOnly": return WellKnownType.DateOnly;
                    case "TimeOnly": return WellKnownType.TimeOnly;
                }
            }
        }

        return WellKnownType.None;
    }

    private static bool IsPrimitiveOrWellKnownType(ITypeSymbol type)
    {
        return ClassifyWellKnownType(type) != WellKnownType.None;
    }

    /// <summary>
    /// Checks if a type is allowed as a dictionary key.
    /// Keys must be primitive, string, enum, or well-known value types.
    /// </summary>
    private static bool IsAllowedDictionaryKeyType(ITypeSymbol type)
    {
        // Primitives and string
        if (IsPrimitiveOrWellKnownType(type))
            return true;

        // String
        if (type.SpecialType == SpecialType.System_String)
            return true;

        // Enums
        if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum)
            return true;

        return false;
    }
}
