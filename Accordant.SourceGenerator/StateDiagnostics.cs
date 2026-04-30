// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// Diagnostic descriptors for the State source generator.
    /// </summary>
    public static class StateDiagnostics
    {
        private const string Category = "Microsoft.Accordant.State";

        /// <summary>
        /// STATE001: Interface collection types (IList, IDictionary, etc.) are not supported.
        /// Use concrete types like List&lt;T&gt; or Dictionary&lt;TKey, TValue&gt; instead.
        /// </summary>
        public static readonly DiagnosticDescriptor InterfaceCollectionType = new(
            id: "STATE001",
            title: "Interface collection type not supported",
            messageFormat: "Property '{0}' uses interface type '{1}'; use concrete type like List<T> or Dictionary<TKey, TValue> instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "State properties cannot use interface collection types. Use concrete types like List<T> or Dictionary<TKey, TValue>.");

        /// <summary>
        /// STATE002: Unconstrained generic type parameters are not supported.
        /// Generic type parameters must be constrained to State.
        /// </summary>
        public static readonly DiagnosticDescriptor UnconstrainedGenericType = new(
            id: "STATE002",
            title: "Unconstrained generic type parameter",
            messageFormat: "Property '{0}' uses generic type parameter '{1}' which is not constrained to State; add 'where {1} : State' constraint",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Generic type parameters used in State class properties must be constrained to State type.");

        /// <summary>
        /// STATE003: Unsupported external type.
        /// The type is not a primitive, well-known type, collection, or State class.
        /// </summary>
        public static readonly DiagnosticDescriptor UnsupportedExternalType = new(
            id: "STATE003",
            title: "Unsupported type",
            messageFormat: "Property '{0}' uses unsupported type '{1}'; supported types: primitives (int, bool, string, etc.), DateTime, Guid, TimeSpan, enums, arrays, List<T>, Dictionary<K,V>, tuples, and [State] classes",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "State properties must use supported types that can be cloned and fingerprinted deterministically.");

        /// <summary>
        /// STATE004: [State] attribute used on struct.
        /// State classes must be classes, not structs.
        /// </summary>
        public static readonly DiagnosticDescriptor StateOnStruct = new(
            id: "STATE004",
            title: "[State] attribute on struct",
            messageFormat: "The [State] attribute cannot be applied to struct '{0}'; use a class instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The [State] attribute can only be applied to classes, not structs.");

        /// <summary>
        /// Alias for StateOnStruct for consistency with DiagnosticKind.StructNotSupported
        /// </summary>
        public static DiagnosticDescriptor StructNotSupported => StateOnStruct;

        /// <summary>
        /// STATE005: Missing partial keyword.
        /// State classes must be declared as partial.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingPartialKeyword = new(
            id: "STATE005",
            title: "Missing partial keyword",
            messageFormat: "The [State] class '{0}' must be declared as partial",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Classes marked with [State] must be declared as partial to allow the source generator to extend them.");

        /// <summary>
        /// STATE006: [State] attribute used on record.
        /// Records cannot inherit from the State class.
        /// </summary>
        public static readonly DiagnosticDescriptor StateOnRecord = new(
            id: "STATE006",
            title: "[State] attribute on record",
            messageFormat: "The [State] attribute cannot be applied to record '{0}' because records cannot inherit from State; use a class instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The [State] attribute can only be applied to classes. Records cannot inherit from the State class.");

        /// <summary>
        /// STATE007: [State] class is nested inside another type.
        /// State classes must be declared at namespace level.
        /// </summary>
        public static readonly DiagnosticDescriptor NestedStateClass = new(
            id: "STATE007",
            title: "Nested State class not supported",
            messageFormat: "The [State] class '{0}' cannot be nested inside another type; move it to namespace level",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "State classes must be declared at namespace level, not nested inside other types.");

        /// <summary>
        /// STATE008: State class is missing a parameterless constructor.
        /// State classes must have an accessible parameterless constructor.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingParameterlessConstructor = new(
            id: "STATE008",
            title: "Missing parameterless constructor",
            messageFormat: "The [State] class '{0}' must have an accessible parameterless constructor",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "State classes must have a public or internal parameterless constructor for cloning.");

        /// <summary>
        /// STATE009: Dictionary key type is not supported.
        /// Dictionary keys must be primitive, string, enum, or well-known value type.
        /// </summary>
        public static readonly DiagnosticDescriptor UnsupportedDictionaryKeyType = new(
            id: "STATE009",
            title: "Unsupported dictionary key type",
            messageFormat: "Property '{0}' uses dictionary with unsupported key type '{1}'; keys must be primitive, string, enum, or well-known value type (DateTime, Guid, etc.)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Dictionary keys in State classes must be primitive, string, enum, or well-known value types like DateTime, Guid, TimeSpan.");

        /// <summary>
        /// STATE010: Init-only property not supported.
        /// Properties with init accessors cannot be assigned in Clone.
        /// </summary>
        public static readonly DiagnosticDescriptor InitOnlyProperty = new(
            id: "STATE010",
            title: "Init-only property not supported",
            messageFormat: "Property '{0}' has an init-only setter which is not supported in [State] classes; use a regular setter instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Init-only properties cannot be assigned after object construction, which prevents cloning.");

        /// <summary>
        /// STATE011: Required property not supported.
        /// Required members need special handling in object construction.
        /// </summary>
        public static readonly DiagnosticDescriptor RequiredProperty = new(
            id: "STATE011",
            title: "Required property not supported",
            messageFormat: "Property '{0}' is marked as required which is not supported in [State] classes; remove the required modifier",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Required properties need special initialization which is incompatible with the generated Clone method.");

        /// <summary>
        /// STATE012: Multidimensional array not supported.
        /// Only single-dimension arrays are supported.
        /// </summary>
        public static readonly DiagnosticDescriptor MultidimensionalArray = new(
            id: "STATE012",
            title: "Multidimensional array not supported",
            messageFormat: "Property '{0}' uses multidimensional array '{1}' which is not supported; use jagged arrays (T[][]) or List<List<T>> instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Multidimensional arrays (T[,]) are not supported. Use jagged arrays (T[][]) instead.");

        /// <summary>
        /// STATE013: Non-[State] State base class.
        /// If inheriting from a State subclass, that class must also have [State] attribute.
        /// </summary>
        public static readonly DiagnosticDescriptor NonStateBaseClass = new(
            id: "STATE013",
            title: "Base class missing [State] attribute",
            messageFormat: "Class '{0}' inherits from '{1}' which derives from State but is not marked with [State]; add [State] to '{1}' or inherit directly from State",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "When inheriting from a State subclass, that class must also be marked with [State] to ensure the generated methods work correctly.");

        /// <summary>
        /// STATE014: Property hides base class property.
        /// Using 'new' to hide a base property causes it to be omitted from generated methods.
        /// </summary>
        public static readonly DiagnosticDescriptor HiddenBaseProperty = new(
            id: "STATE014",
            title: "Property hides base class property",
            messageFormat: "Property '{0}' in class '{1}' hides a property from base class; remove the 'new' modifier or rename the property",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Properties that hide base class properties using 'new' will cause the base property to be omitted from generated methods.");

        /// <summary>
        /// STATE015: Abstract class cannot have [State] attribute.
        /// Cannot generate new ClassName() for abstract classes.
        /// </summary>
        public static readonly DiagnosticDescriptor AbstractStateClass = new(
            id: "STATE015",
            title: "Abstract class cannot be a State",
            messageFormat: "Abstract class '{0}' cannot have [State] attribute; only concrete classes are supported",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The [State] attribute generates Clone() which requires instantiation with 'new'. Abstract classes cannot be instantiated.");

        /// <summary>
        /// STATE016: Getter-only property of mutable type.
        /// Cannot clone properties that have no setter but can be mutated.
        /// </summary>
        public static readonly DiagnosticDescriptor GetterOnlyMutableProperty = new(
            id: "STATE016",
            title: "Getter-only property of mutable type",
            messageFormat: "Property '{0}' in class '{1}' is getter-only but has mutable type '{2}'; add a setter or change to an immutable type",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Properties without setters cannot be cloned. If the property type is mutable (List, Dictionary, State, etc.), the contents can be modified but not properly cloned, leading to incorrect behavior.");

        /// <summary>
        /// STATE017: Instance field in State class.
        /// Fields are not included in generated methods - use properties instead.
        /// </summary>
        public static readonly DiagnosticDescriptor InstanceFieldNotAllowed = new(
            id: "STATE017",
            title: "Instance field not allowed in State class",
            messageFormat: "Field '{0}' in class '{1}' is not allowed; use a property instead (only const and static fields are permitted)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Instance fields in [State] classes are not cloned, hashed, or frozen. This can lead to subtle bugs where state appears to be copied but fields are shared. Use properties instead.");

        /// <summary>
        /// STATE018: Property type is a [State] class that has errors.
        /// Generated code would fail to compile because the property type won't have Clone() etc.
        /// </summary>
        public static readonly DiagnosticDescriptor PropertyTypeHasErrors = new(
            id: "STATE018",
            title: "Property type has generation errors",
            messageFormat: "Property '{0}' references State class '{1}' which has errors and won't generate correctly",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The property references a [State] class that has its own errors (unsupported types, missing partial, etc.). Fix the errors in the referenced class first.");

        /// <summary>
        /// STATE019: Base [State] class has errors.
        /// Generated code would fail to compile because the base class won't have Clone() etc.
        /// </summary>
        public static readonly DiagnosticDescriptor BaseTypeHasErrors = new(
            id: "STATE019",
            title: "Base [State] class has generation errors",
            messageFormat: "Base class '{0}' has errors and won't generate correctly",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The base [State] class has its own errors (unsupported types, missing partial, etc.). Fix the errors in the base class first.");

        // STATE020 is reserved (was: non-public setter error, removed because generated code
        // is in same partial class and can access private/internal setters)

        /// <summary>
        /// STATE021: Invalid base class.
        /// [State] classes must inherit from object, State, or another [State] class.
        /// </summary>
        public static readonly DiagnosticDescriptor InvalidBaseClass = new(
            id: "STATE021",
            title: "Invalid base class for [State]",
            messageFormat: "Class '{0}' inherits from '{1}' which is not a [State] class; [State] classes must inherit from object, State, or another [State] class",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "[State] classes can only inherit from object (implicit), Accordant.State, or another class marked with [State]. Inheriting from other classes would cause conflicts in the generated partial class.");

        /// <summary>
        /// STATE022: [SharedState] on unsupported external type requires Fingerprint method.
        /// </summary>
        public static readonly DiagnosticDescriptor SharedStateRequiresFingerprint = new(
            id: "STATE022",
            title: "[SharedState] requires Fingerprint for unsupported types",
            messageFormat: "Property '{0}' has [SharedState] but type '{1}' cannot be hashed; specify a Fingerprint method: [SharedState(Fingerprint = nameof(YourMethod))]",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "[SharedState] properties of unsupported external types must specify a custom Fingerprint method to compute the state hash. Supported types (primitives, collections, [State] classes) do not require a Fingerprint.");

        /// <summary>
        /// STATE023: [SharedState] Fingerprint method not found.
        /// </summary>
        public static readonly DiagnosticDescriptor SharedStateFingerprintNotFound = new(
            id: "STATE023",
            title: "[SharedState] Fingerprint method not found",
            messageFormat: "Property '{0}' specifies Fingerprint method '{1}' which was not found in class '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The Fingerprint method specified in [SharedState] must exist in the containing class.");

        /// <summary>
        /// STATE024: [SharedState] Fingerprint method has wrong signature.
        /// </summary>
        public static readonly DiagnosticDescriptor SharedStateFingerprintWrongSignature = new(
            id: "STATE024",
            title: "[SharedState] Fingerprint method has wrong signature",
            messageFormat: "Property '{0}' Fingerprint method '{1}' must have signature 'string {1}({2} value)' or be a string property",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The Fingerprint method must accept the property type as parameter and return string, or be a property that returns string.");

        /// <summary>
        /// STATE025: [SharedState] on value type is not useful.
        /// </summary>
        public static readonly DiagnosticDescriptor SharedStateOnValueType = new(
            id: "STATE025",
            title: "[SharedState] on value type",
            messageFormat: "Property '{0}' of value type '{1}' cannot use [SharedState] because value types are always copied by value",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "[SharedState] is for sharing references. Value types (int, struct, enum, etc.) are always copied by value, so [SharedState] has no effect.");
    }
}
