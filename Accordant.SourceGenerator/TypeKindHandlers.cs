// Copyright (c) Accordant. All rights reserved.
// Licensed under the MIT license.

namespace Microsoft.Accordant.SourceGenerator
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Text;

    // ========================================================================
    // TypeKindHandler: Abstract base for per-TypeKind code generation
    // ========================================================================

    /// <summary>
    /// Base class for type-kind-specific code generation.
    /// Each TypeKind value gets one handler that co-locates all 4 operations
    /// (Clone, Hash, Fingerprint, Freeze), eliminating parallel switch statements.
    /// </summary>
    internal abstract class TypeKindHandler
    {
        /// <summary>Generates a C# expression that clones a value of this type.</summary>
        public abstract string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName);

        /// <summary>Appends hash computation code for a value of this type.</summary>
        public abstract void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth);

        /// <summary>Generates a C# expression that produces a fingerprint string for this type.</summary>
        public abstract string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr);

        /// <summary>Appends freeze code for a value of this type (only for types containing State classes).</summary>
        public abstract void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth);

        // ====================================================================
        // Hash helpers (used by String, Nullable, StateClass, Dictionary, Collection handlers)
        // ====================================================================

        /// <summary>
        /// Emits the start of a null-checked hash block with a non-null tag byte.
        /// Must be paired with EndNullCheckedHash.
        /// </summary>
        protected internal static void StartNullCheckedHash(StringBuilder sb, string indent, string condition)
        {
            sb.AppendLine($"{indent}if ({condition})");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    hasher.Append(new byte[] {{ 1 }}); // non-null tag");
        }

        /// <summary>
        /// Emits the end of a null-checked hash block with a null tag byte for the else branch.
        /// </summary>
        protected internal static void EndNullCheckedHash(StringBuilder sb, string indent)
        {
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}else hasher.Append(new byte[] {{ 0 }}); // null tag");
        }

        // ====================================================================
        // Freeze helpers
        // ====================================================================

        /// <summary>
        /// Returns true if this type can contain State classes that need freezing.
        /// Handlers override this to answer for their specific type kind.
        /// Default: false (primitives, strings, enums don't contain State classes).
        /// </summary>
        public virtual bool ContainsStateClass(TypeInfo typeInfo) => false;
    }

    // ========================================================================
    // TypeKindDispatch: Registry + dispatch for recursive type handling
    // ========================================================================

    /// <summary>
    /// Central dispatch for type-kind-specific code generation.
    /// Handlers call back through these static methods for recursive type handling
    /// (e.g., NullableHandler dispatching to its inner type).
    /// </summary>
    internal static class TypeKindDispatch
    {
        private static readonly Dictionary<TypeKind, TypeKindHandler> Handlers;

        static TypeKindDispatch()
        {
            Handlers = new Dictionary<TypeKind, TypeKindHandler>
            {
                { TypeKind.Primitive, new PrimitiveHandler() },
                { TypeKind.String, new StringHandler() },
                { TypeKind.Enum, new EnumHandler() },
                { TypeKind.Nullable, new NullableHandler() },
                { TypeKind.StateClass, new StateClassHandler() },
                { TypeKind.Dictionary, new DictionaryHandler() },
                { TypeKind.List, new ListHandler() },
                { TypeKind.Array, new ArrayHandler() },
                { TypeKind.ValueTuple, new ValueTupleHandler() },
                { TypeKind.Unknown, new UnknownHandler() },
                { TypeKind.UnsupportedInterface, new UnsupportedHandler() },
                { TypeKind.UnsupportedGeneric, new UnsupportedHandler() },
            };

            // Validate completeness: every TypeKind must have a handler
            foreach (TypeKind kind in Enum.GetValues(typeof(TypeKind)))
            {
                if (!Handlers.ContainsKey(kind))
                    throw new InvalidOperationException($"No TypeKindHandler registered for TypeKind.{kind}");
            }
        }

        public static string Clone(TypeInfo typeInfo, string valueExpr, string mapName)
            => Handlers[typeInfo.Kind].GenerateClone(typeInfo, valueExpr, mapName);

        public static void Hash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
            => Handlers[typeInfo.Kind].GenerateHash(sb, typeInfo, valueExpr, indent, depth);

        public static string Fingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => Handlers[typeInfo.Kind].GenerateFingerprint(typeInfo, valueExpr, pathsName, pathExpr);

        public static void Freeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
            => Handlers[typeInfo.Kind].GenerateFreeze(sb, typeInfo, valueExpr, indent, depth);

        public static bool ContainsStateClass(TypeInfo typeInfo)
            => Handlers[typeInfo.Kind].ContainsStateClass(typeInfo);
    }

    // ========================================================================
    // Concrete Handlers
    // ========================================================================

    /// <summary>Handles TypeKind.Primitive — immutable value types (int, bool, DateTime, etc.).</summary>
    internal sealed class PrimitiveHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => valueExpr; // immutable, copy by value

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            switch (typeInfo.WellKnownType)
            {
                case WellKnownType.Int32:
                case WellKnownType.Int64:
                case WellKnownType.Int16:
                case WellKnownType.UInt32:
                case WellKnownType.UInt64:
                case WellKnownType.UInt16:
                case WellKnownType.Single:
                case WellKnownType.Double:
                case WellKnownType.Boolean:
                case WellKnownType.Char:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}));");
                    break;

                case WellKnownType.Byte:
                    sb.AppendLine($"{indent}hasher.Append(new byte[] {{ {valueExpr} }});");
                    break;

                case WellKnownType.SByte:
                    sb.AppendLine($"{indent}hasher.Append(new byte[] {{ unchecked((byte){valueExpr}) }});");
                    break;

                case WellKnownType.Decimal:
                    sb.AppendLine($"{indent}foreach (var b in decimal.GetBits({valueExpr})) hasher.Append(BitConverter.GetBytes(b));");
                    break;

                case WellKnownType.Guid:
                    sb.AppendLine($"{indent}hasher.Append({valueExpr}.ToByteArray());");
                    break;

                case WellKnownType.DateTime:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.Ticks));");
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes((int){valueExpr}.Kind));");
                    break;

                case WellKnownType.DateTimeOffset:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.Ticks));");
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.Offset.Ticks));");
                    break;

                case WellKnownType.TimeSpan:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.Ticks));");
                    break;

                case WellKnownType.DateOnly:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.DayNumber));");
                    break;

                case WellKnownType.TimeOnly:
                    sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes({valueExpr}.Ticks));");
                    break;

                case WellKnownType.None:
                    throw new InvalidOperationException(
                        $"PrimitiveHandler should not receive WellKnownType.None for expression '{valueExpr}'");

                default:
                    // Fallback for unknown primitives: use invariant string representation
                    sb.AppendLine($"{indent}hasher.Append(Encoding.UTF8.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0}}\", {valueExpr})));");
                    break;
            }
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
        {
            switch (typeInfo.WellKnownType)
            {
                case WellKnownType.DateTime:
                case WellKnownType.DateTimeOffset:
                case WellKnownType.TimeOnly:
                case WellKnownType.DateOnly:
                    return $"{valueExpr}.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)";

                // G17: 17 significant digits for double (IEEE 754 has ~15.95 digits)
                case WellKnownType.Double:
                    return $"{valueExpr}.ToString(\"G17\", System.Globalization.CultureInfo.InvariantCulture)";

                // G9: 9 significant digits for float (IEEE 754 has ~7.22 digits)
                case WellKnownType.Single:
                    return $"{valueExpr}.ToString(\"G9\", System.Globalization.CultureInfo.InvariantCulture)";

                case WellKnownType.None:
                    throw new InvalidOperationException(
                        $"PrimitiveHandler should not receive WellKnownType.None for expression '{valueExpr}'");

                default:
                    return $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0}}\", {valueExpr})";
            }
        }

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Primitives are immutable — nothing to freeze
        }
    }

    /// <summary>Handles TypeKind.String — immutable reference type.</summary>
    internal sealed class StringHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => valueExpr; // strings are immutable

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            StartNullCheckedHash(sb, indent, $"{valueExpr} != null");
            sb.AppendLine($"{indent}    global::Microsoft.Accordant.State.AppendLengthPrefixedString(hasher, {valueExpr});");
            EndNullCheckedHash(sb, indent);
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => StringEscaper.GenerateEscapedStringExpression(valueExpr);

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Strings are immutable — nothing to freeze
        }
    }

    /// <summary>Handles TypeKind.Enum — immutable value types.</summary>
    internal sealed class EnumHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => valueExpr; // enums are immutable

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Use long cast to handle all enum underlying types (byte, short, int, long, etc.)
            sb.AppendLine($"{indent}hasher.Append(BitConverter.GetBytes((long){valueExpr}));");
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0}}\", {valueExpr})";

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Enums are immutable — nothing to freeze
        }
    }

    /// <summary>Handles TypeKind.Nullable — wraps an inner type with null check.</summary>
    internal sealed class NullableHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
        {
            var innerClone = TypeKindDispatch.Clone(typeInfo.InnerType!, $"{valueExpr}.Value", mapName);
            return $"{valueExpr}.HasValue ? {innerClone} : null";
        }

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            StartNullCheckedHash(sb, indent, $"{valueExpr}.HasValue");
            TypeKindDispatch.Hash(sb, typeInfo.InnerType!, $"{valueExpr}.Value", indent + "    ", depth);
            EndNullCheckedHash(sb, indent);
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
        {
            var innerFp = TypeKindDispatch.Fingerprint(typeInfo.InnerType!, $"{valueExpr}.Value", pathsName, pathExpr);
            return $"({valueExpr}.HasValue ? {innerFp} : \"null\")";
        }

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var innerType = typeInfo.InnerType!;
            if (TypeKindDispatch.ContainsStateClass(innerType))
            {
                sb.AppendLine($"{indent}{valueExpr}?.Freeze(visited);");
            }
        }

        public override bool ContainsStateClass(TypeInfo typeInfo)
            => typeInfo.InnerType != null && TypeKindDispatch.ContainsStateClass(typeInfo.InnerType);
    }

    /// <summary>Handles TypeKind.StateClass — [State]-attributed classes with Clone/Hash/Fingerprint/Freeze support.</summary>
    internal sealed class StateClassHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => $"{valueExpr} == null ? null : ({typeInfo.TypeName}){valueExpr}.Clone({mapName})";

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            StartNullCheckedHash(sb, indent, $"{valueExpr} != null");
            sb.AppendLine($"{indent}    {valueExpr}.AppendHashCore(hasher, visited);");
            EndNullCheckedHash(sb, indent);
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => $"({valueExpr}?.StringRepresentation({pathsName}, {pathExpr}, forceRecompute) ?? \"null\")";

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            sb.AppendLine($"{indent}{valueExpr}?.Freeze(visited);");
        }

        public override bool ContainsStateClass(TypeInfo typeInfo) => true;
    }

    /// <summary>Handles TypeKind.Dictionary — Dictionary&lt;K,V&gt; with comparer preservation.</summary>
    internal sealed class DictionaryHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
        {
            var keyType = typeInfo.InnerType!;
            var valueType = typeInfo.SecondaryType!;

            var keyClone = TypeKindDispatch.Clone(keyType, "kvp.Key", mapName);
            var valClone = TypeKindDispatch.Clone(valueType, "kvp.Value", mapName);

            if (valClone == "kvp.Value" && keyClone == "kvp.Key")
            {
                return $"{valueExpr} == null ? null : new {typeInfo.NonNullableTypeName}({valueExpr}, {valueExpr}.Comparer)";
            }

            return $"{valueExpr} == null ? null : {valueExpr}.Aggregate(new {typeInfo.NonNullableTypeName}({valueExpr}.Comparer), (d, kvp) => {{ d[{keyClone}] = {valClone}; return d; }})";
        }

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var kvpVar = $"kvp{depth}";
            var keyType = typeInfo.InnerType!;
            var valType = typeInfo.SecondaryType!;

            StartNullCheckedHash(sb, indent, $"{valueExpr} != null");
            sb.AppendLine($"{indent}    hasher.Append(BitConverter.GetBytes({valueExpr}.Count));");

            // Sort keys for deterministic ordering
            var orderByClause = keyType.Kind == TypeKind.String
                ? $"{valueExpr}.OrderBy(x => x.Key, StringComparer.Ordinal)"
                : $"{valueExpr}.OrderBy(x => x.Key)";

            sb.AppendLine($"{indent}    foreach (var {kvpVar} in {orderByClause})");
            sb.AppendLine($"{indent}    {{");
            TypeKindDispatch.Hash(sb, keyType, $"{kvpVar}.Key", indent + "        ", depth + 1);
            TypeKindDispatch.Hash(sb, valType, $"{kvpVar}.Value", indent + "        ", depth + 1);
            sb.AppendLine($"{indent}    }}");
            EndNullCheckedHash(sb, indent);
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
        {
            var keyType = typeInfo.InnerType!;
            var valueType = typeInfo.SecondaryType!;

            var keyFp = TypeKindDispatch.Fingerprint(keyType, "kvp.Key", pathsName, pathExpr);

            // Escape string keys for path expressions
            string keyPathExpr;
            if (keyType.Kind == TypeKind.String)
            {
                keyPathExpr = StringEscaper.GeneratePathKeyExpression("kvp.Key", pathExpr);
            }
            else
            {
                keyPathExpr = $"{pathExpr} + \"[\" + string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0}}\", kvp.Key) + \"]\"";
            }

            var valFp = TypeKindDispatch.Fingerprint(valueType, "kvp.Value", pathsName, keyPathExpr);

            var orderBy = keyType.Kind == TypeKind.String
                ? $"{valueExpr}.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)"
                : $"{valueExpr}.OrderBy(kvp => kvp.Key)";

            return $"({valueExpr} == null ? \"null\" : \"{{\" + string.Join(\", \", {orderBy}.Select(kvp => ({keyFp}) + \"=\" + ({valFp}))) + \"}}\")";
        }

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var valType = typeInfo.SecondaryType!;
            if (TypeKindDispatch.ContainsStateClass(valType))
            {
                var itemVar = depth == 0 ? "item" : $"item{depth}";
                sb.AppendLine($"{indent}if ({valueExpr} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    foreach (var {itemVar} in {valueExpr}.Values)");
                TypeKindDispatch.Freeze(sb, valType, itemVar, indent + "        ", depth + 1);
                sb.AppendLine($"{indent}}}");
            }
        }

        public override bool ContainsStateClass(TypeInfo typeInfo)
            => typeInfo.SecondaryType != null && TypeKindDispatch.ContainsStateClass(typeInfo.SecondaryType);
    }

    /// <summary>Shared base for List and Arrayhandlers — both are ordered collections with an inner element type.</summary>
    internal abstract class CollectionHandlerBase : TypeKindHandler
    {
        protected abstract string GetCountExpression(string valueExpr);
        protected abstract string GenerateSimpleClone(TypeInfo typeInfo, string valueExpr);
        protected abstract string GenerateElementClone(TypeInfo typeInfo, string valueExpr, string elementClone);

        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
        {
            var elementType = typeInfo.InnerType!;
            var elementClone = TypeKindDispatch.Clone(elementType, "item", mapName);

            if (elementClone == "item")
                return GenerateSimpleClone(typeInfo, valueExpr);

            return GenerateElementClone(typeInfo, valueExpr, elementClone);
        }

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var itemVar = $"item{depth}";
            var elemType = typeInfo.InnerType!;

            StartNullCheckedHash(sb, indent, $"{valueExpr} != null");
            sb.AppendLine($"{indent}    hasher.Append(BitConverter.GetBytes({GetCountExpression(valueExpr)}));");
            sb.AppendLine($"{indent}    foreach (var {itemVar} in {valueExpr})");
            sb.AppendLine($"{indent}    {{");
            TypeKindDispatch.Hash(sb, elemType, itemVar, indent + "        ", depth + 1);
            sb.AppendLine($"{indent}    }}");
            EndNullCheckedHash(sb, indent);
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
        {
            var elementType = typeInfo.InnerType!;
            var elementFp = TypeKindDispatch.Fingerprint(elementType, "item", pathsName, $"{pathExpr} + \"[\" + i + \"]\"");
            return $"({valueExpr} == null ? \"null\" : \"[\" + string.Join(\", \", {valueExpr}.Select((item, i) => {elementFp})) + \"]\")";
        }

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var elemType = typeInfo.InnerType!;
            if (TypeKindDispatch.ContainsStateClass(elemType))
            {
                var itemVar = depth == 0 ? "item" : $"item{depth}";
                sb.AppendLine($"{indent}if ({valueExpr} != null)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    foreach (var {itemVar} in {valueExpr})");
                TypeKindDispatch.Freeze(sb, elemType, itemVar, indent + "        ", depth + 1);
                sb.AppendLine($"{indent}}}");
            }
        }

        public override bool ContainsStateClass(TypeInfo typeInfo)
            => typeInfo.InnerType != null && TypeKindDispatch.ContainsStateClass(typeInfo.InnerType);
    }

    /// <summary>Handles TypeKind.List — List&lt;T&gt;.</summary>
    internal sealed class ListHandler : CollectionHandlerBase
    {
        protected override string GetCountExpression(string valueExpr) => $"{valueExpr}.Count";

        protected override string GenerateSimpleClone(TypeInfo typeInfo, string valueExpr)
            => $"{valueExpr} == null ? null : new {typeInfo.NonNullableTypeName}({valueExpr})";

        protected override string GenerateElementClone(TypeInfo typeInfo, string valueExpr, string elementClone)
            => $"{valueExpr} == null ? null : new {typeInfo.NonNullableTypeName}({valueExpr}.Select(item => {elementClone}))";
    }

    /// <summary>Handles TypeKind.Array — T[].</summary>
    internal sealed class ArrayHandler : CollectionHandlerBase
    {
        protected override string GetCountExpression(string valueExpr) => $"{valueExpr}.Length";

        protected override string GenerateSimpleClone(TypeInfo typeInfo, string valueExpr)
            => $"{valueExpr} == null ? null : ({typeInfo.NonNullableTypeName}){valueExpr}.Clone()";

        protected override string GenerateElementClone(TypeInfo typeInfo, string valueExpr, string elementClone)
            => $"{valueExpr}?.Select(item => {elementClone}).ToArray()";
    }

    /// <summary>Handles TypeKind.ValueTuple — (T1, T2, ...) value tuples.</summary>
    internal sealed class ValueTupleHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
        {
            var elements = typeInfo.TupleElements!.Value;
            var clonedElements = new List<string>();

            for (int i = 0; i < elements.Length; i++)
            {
                var itemExpr = $"{valueExpr}.Item{i + 1}";
                var cloned = TypeKindDispatch.Clone(elements[i], itemExpr, mapName);
                clonedElements.Add(cloned);
            }

            return $"({string.Join(", ", clonedElements)})";
        }

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var tupleElements = typeInfo.TupleElements!.Value;
            for (int i = 0; i < tupleElements.Length; i++)
            {
                var elem = tupleElements[i];
                var itemName = $"Item{i + 1}";
                TypeKindDispatch.Hash(sb, elem, $"{valueExpr}.{itemName}", indent, depth);
            }
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
        {
            var elements = typeInfo.TupleElements!.Value;
            var fpParts = new List<string>();

            for (int i = 0; i < elements.Length; i++)
            {
                var itemExpr = $"{valueExpr}.Item{i + 1}";
                var fp = TypeKindDispatch.Fingerprint(elements[i], itemExpr, pathsName, $"{pathExpr} + \".Item{i + 1}\"");
                fpParts.Add(fp);
            }

            return $"\"(\" + {string.Join(" + \", \" + ", fpParts)} + \")\"";
        }

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            var elements = typeInfo.TupleElements!.Value;
            for (int i = 0; i < elements.Length; i++)
            {
                var elemInfo = elements[i];
                if (TypeKindDispatch.ContainsStateClass(elemInfo))
                {
                    TypeKindDispatch.Freeze(sb, elemInfo, $"{valueExpr}.Item{i + 1}", indent, depth);
                }
            }
        }

        public override bool ContainsStateClass(TypeInfo typeInfo)
            => typeInfo.TupleElements != null &&
               typeInfo.TupleElements.Value.Any(e => TypeKindDispatch.ContainsStateClass(e));
    }

    /// <summary>Handles TypeKind.Unknown — unrecognized types that still get generated code (with warnings).</summary>
    internal sealed class UnknownHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => $"{valueExpr} /* WARNING: Unknown type, using shallow copy */";

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            sb.AppendLine($"{indent}hasher.Append(Encoding.UTF8.GetBytes({valueExpr}?.ToString() ?? \"null\"));");
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => StringEscaper.GenerateEscapedStringExpression($"{valueExpr}?.ToString()");

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Unknown types — can't freeze
        }
    }

    /// <summary>Handles TypeKind.UnsupportedInterface and TypeKind.UnsupportedGeneric —
    /// should not reach codegen (errors prevent generation), but provides safe fallback.</summary>
    internal sealed class UnsupportedHandler : TypeKindHandler
    {
        public override string GenerateClone(TypeInfo typeInfo, string valueExpr, string mapName)
            => valueExpr; // identity — errors should prevent this from being reached

        public override void GenerateHash(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            sb.AppendLine($"{indent}hasher.Append(Encoding.UTF8.GetBytes({valueExpr}?.ToString() ?? \"null\"));");
        }

        public override string GenerateFingerprint(TypeInfo typeInfo, string valueExpr, string pathsName, string pathExpr)
            => StringEscaper.GenerateEscapedStringExpression($"{valueExpr}?.ToString()");

        public override void GenerateFreeze(StringBuilder sb, TypeInfo typeInfo, string valueExpr, string indent, int depth)
        {
            // Unsupported types — can't freeze
        }
    }
}
