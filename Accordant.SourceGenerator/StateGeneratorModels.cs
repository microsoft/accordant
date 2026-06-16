// Copyright (c) Accordant. All rights reserved.
// Licensed under the MIT license.

namespace Microsoft.Accordant.SourceGenerator;

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

/// <summary>
/// Represents the kind of type for code generation purposes.
/// </summary>
internal enum TypeKind
{
    Primitive,
    String,
    Enum,
    Nullable,
    StateClass,
    Dictionary,
    List,
    Array,
    ValueTuple,
    Unknown,
    UnsupportedInterface,
    UnsupportedGeneric
}

/// <summary>
/// Normalized primitive/well-known type kind for code generation.
/// Eliminates string-based type matching in hash/fingerprint emitters.
/// </summary>
internal enum WellKnownType
{
    None,       // Not a well-known type
    Boolean,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    Char,
    DateTime,
    DateTimeOffset,
    TimeSpan,
    Guid,
    DateOnly,
    TimeOnly
}

/// <summary>
/// Represents different kinds of diagnostics the generator can emit.
/// </summary>
internal enum DiagnosticKind
{
    InterfaceCollectionType,
    UnconstrainedGenericType,
    UnsupportedExternalType,
    NestedStateClass,
    MissingParameterlessConstructor,
    UnsupportedDictionaryKeyType,
    InitOnlyProperty,
    RequiredProperty,
    MultidimensionalArray,
    NonStateBaseClass,
    HiddenBaseProperty,
    AbstractStateClass,
    GetterOnlyMutableProperty,
    InstanceFieldNotAllowed,
    StructNotSupported,
    InvalidBaseClass,
    SharedStateRequiresFingerprint,
    SharedStateFingerprintNotFound,
    SharedStateFingerprintWrongSignature,
    SharedStateOnValueType
}

/// <summary>
/// Information about a diagnostic to be reported.
/// </summary>
internal sealed class DiagnosticInfo
{
    public DiagnosticKind Kind { get; }
    /// <summary>Name of the symbol (property, field, or class) this diagnostic relates to.</summary>
    public string SymbolName { get; }
    public string TypeName { get; }
    /// <summary>Additional context argument. Usage varies by diagnostic kind:
    /// - GetterOnlyMutableProperty: the mutable type name (3rd message placeholder)
    /// - InstanceFieldNotAllowed: the containing class name (3rd message placeholder)
    /// - SharedStateFingerprintNotFound: the containing class name (3rd message placeholder)
    /// - SharedStateFingerprintWrongSignature: the fingerprint member name (3rd message placeholder)
    /// </summary>
    public string? ContextArg { get; }
    public Location? Location { get; }

    private DiagnosticInfo(DiagnosticKind kind, string symbolName, string? typeName, Location? location, string? contextArg)
    {
        Kind = kind;
        SymbolName = symbolName;
        TypeName = typeName ?? "";
        ContextArg = contextArg;
        Location = location;
    }

    /// <summary>Creates a diagnostic that only needs symbol name and type name.</summary>
    public static DiagnosticInfo Create(DiagnosticKind kind, string symbolName, string? typeName, Location? location = null)
        => new DiagnosticInfo(kind, symbolName, typeName, location, contextArg: null);

    /// <summary>Getter-only property with mutable type (STATE016). ContextArg = mutable type display name.</summary>
    public static DiagnosticInfo GetterOnlyMutableProperty(string propertyName, string typeName, Location? location, string className)
        => new DiagnosticInfo(DiagnosticKind.GetterOnlyMutableProperty, propertyName, typeName, location, contextArg: className);

    /// <summary>Instance field not allowed (STATE017). ContextArg = containing class name.</summary>
    public static DiagnosticInfo InstanceFieldNotAllowed(string fieldName, string className, Location? location)
        => new DiagnosticInfo(DiagnosticKind.InstanceFieldNotAllowed, fieldName, className, location, contextArg: className);

    /// <summary>Fingerprint member not found (STATE023). ContextArg = containing class name.</summary>
    public static DiagnosticInfo FingerprintNotFound(string propertyName, string fingerprintName, Location? location, string className)
        => new DiagnosticInfo(DiagnosticKind.SharedStateFingerprintNotFound, propertyName, fingerprintName, location, contextArg: className);

    /// <summary>Fingerprint member has wrong signature (STATE024). ContextArg = fingerprint member name.</summary>
    public static DiagnosticInfo FingerprintWrongSignature(string propertyName, string typeName, Location? location, string fingerprintName)
        => new DiagnosticInfo(DiagnosticKind.SharedStateFingerprintWrongSignature, propertyName, typeName, location, contextArg: fingerprintName);
}

/// <summary>
/// Information about a generic type parameter and its constraints.
/// </summary>
internal sealed class TypeParameterInfo : IEquatable<TypeParameterInfo>
{
    public string Name { get; }
    public ImmutableArray<string> Constraints { get; }

    public TypeParameterInfo(string name, ImmutableArray<string> constraints)
    {
        Name = name;
        Constraints = constraints;
    }

    public bool Equals(TypeParameterInfo? other)
    {
        if (other is null) return false;
        if (Name != other.Name) return false;
        if (Constraints.Length != other.Constraints.Length) return false;
        for (int i = 0; i < Constraints.Length; i++)
        {
            if (Constraints[i] != other.Constraints[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TypeParameterInfo);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Name?.GetHashCode() ?? 0;
            foreach (var constraint in Constraints)
            {
                hash = (hash * 397) ^ (constraint?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}

/// <summary>
/// Information about a property's type for code generation.
/// </summary>
internal sealed class TypeInfo : IEquatable<TypeInfo>
{
    public TypeKind Kind { get; }
    public string TypeName { get; }
    /// <summary>
    /// For Primitive types, the normalized well-known type.
    /// Eliminates string-based type matching in code generation.
    /// </summary>
    public WellKnownType WellKnownType { get; }
    public TypeInfo? InnerType { get; }
    public TypeInfo? SecondaryType { get; }
    public ImmutableArray<TypeInfo>? TupleElements { get; }

    /// <summary>
    /// Gets the type name without the nullable annotation (?) suffix.
    /// Used for constructor calls where ? is not valid.
    /// </summary>
    public string NonNullableTypeName => TypeName.TrimEnd('?');

    /// <summary>
    /// Returns true if this type can be hashed natively (without a custom Fingerprint).
    /// Unsupported types (Unknown, UnsupportedInterface, UnsupportedGeneric) require custom handling.
    /// </summary>
    public bool CanBeHashedNatively => Kind != TypeKind.Unknown &&
                                       Kind != TypeKind.UnsupportedInterface &&
                                       Kind != TypeKind.UnsupportedGeneric;

    public TypeInfo(TypeKind kind, string typeName, TypeInfo? innerType = null, TypeInfo? secondaryType = null, WellKnownType wellKnownType = WellKnownType.None)
    {
        Kind = kind;
        TypeName = typeName;
        WellKnownType = wellKnownType;
        InnerType = innerType;
        SecondaryType = secondaryType;
    }

    public TypeInfo(TypeKind kind, string typeName, ImmutableArray<TypeInfo> tupleElements)
    {
        Kind = kind;
        TypeName = typeName;
        TupleElements = tupleElements;
    }

    public bool Equals(TypeInfo? other)
    {
        if (other is null) return false;
        if (Kind != other.Kind || TypeName != other.TypeName) return false;
        if (WellKnownType != other.WellKnownType) return false;
        if (!Equals(InnerType, other.InnerType)) return false;
        if (!Equals(SecondaryType, other.SecondaryType)) return false;
        // Compare TupleElements
        var myTuple = TupleElements ?? ImmutableArray<TypeInfo>.Empty;
        var otherTuple = other.TupleElements ?? ImmutableArray<TypeInfo>.Empty;
        if (myTuple.Length != otherTuple.Length) return false;
        for (int i = 0; i < myTuple.Length; i++)
        {
            if (!Equals(myTuple[i], otherTuple[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TypeInfo);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (Kind.GetHashCode() * 397) ^ (TypeName?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ WellKnownType.GetHashCode();
            hash = (hash * 397) ^ (InnerType?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (SecondaryType?.GetHashCode() ?? 0);
            if (TupleElements.HasValue)
            {
                foreach (var elem in TupleElements.Value)
                {
                    hash = (hash * 397) ^ (elem?.GetHashCode() ?? 0);
                }
            }
            return hash;
        }
    }
}

/// <summary>
/// Represents the shared-state configuration for a property.
/// Replaces convention-based tuple (isSharedState, fingerprintMethod, fingerprintIsMethod)
/// with type-safe alternatives that make invalid states unrepresentable.
/// </summary>
internal abstract class SharedStateInfo : IEquatable<SharedStateInfo>
{
    private SharedStateInfo() { } // Sealed hierarchy

    /// <summary>Property is not shared — normal deep clone/hash/freeze behavior.</summary>
    public static readonly SharedStateInfo NotShared = new NotSharedState();

    /// <summary>Property is shared with no custom fingerprint (uses State's own hash).</summary>
    public static SharedStateInfo SharedNoFingerprint() => new SharedNoFingerprintState();

    /// <summary>Property is shared, fingerprinted via a string property on the containing class.</summary>
    public static SharedStateInfo SharedWithProperty(string propertyName) => new SharedWithPropertyState(propertyName);

    /// <summary>Property is shared, fingerprinted via a method on the containing class.</summary>
    public static SharedStateInfo SharedWithMethod(string methodName) => new SharedWithMethodState(methodName);

    public bool IsShared => !(this is NotSharedState);
    public string? FingerprintMemberName => this is SharedWithPropertyState p ? p.PropertyName
                                          : this is SharedWithMethodState m ? m.MethodName
                                          : null;
    public bool FingerprintIsMethod => this is SharedWithMethodState;

    public abstract bool Equals(SharedStateInfo? other);
    public override bool Equals(object? obj) => Equals(obj as SharedStateInfo);
    public abstract override int GetHashCode();

    private sealed class NotSharedState : SharedStateInfo
    {
        public override bool Equals(SharedStateInfo? other) => other is NotSharedState;
        public override int GetHashCode() => 0;
    }

    private sealed class SharedNoFingerprintState : SharedStateInfo
    {
        public override bool Equals(SharedStateInfo? other) => other is SharedNoFingerprintState;
        public override int GetHashCode() => 1;
    }

    private sealed class SharedWithPropertyState : SharedStateInfo
    {
        public string PropertyName { get; }
        public SharedWithPropertyState(string propertyName) { PropertyName = propertyName; }
        public override bool Equals(SharedStateInfo? other) =>
            other is SharedWithPropertyState p && p.PropertyName == PropertyName;
        public override int GetHashCode() => 2 * 397 ^ (PropertyName?.GetHashCode() ?? 0);
    }

    private sealed class SharedWithMethodState : SharedStateInfo
    {
        public string MethodName { get; }
        public SharedWithMethodState(string methodName) { MethodName = methodName; }
        public override bool Equals(SharedStateInfo? other) =>
            other is SharedWithMethodState m && m.MethodName == MethodName;
        public override int GetHashCode() => 3 * 397 ^ (MethodName?.GetHashCode() ?? 0);
    }
}

/// <summary>
/// Information about a property in a [State] class.
/// </summary>
internal sealed class PropertyInfo : IEquatable<PropertyInfo>
{
    public string Name { get; }
    public string TypeName { get; }
    public TypeInfo TypeInfo { get; }
    /// <summary>Shared-state configuration (type-safe replacement for convention-based booleans).</summary>
    public SharedStateInfo SharedState { get; }

    public PropertyInfo(string name, string typeName, TypeInfo typeInfo, SharedStateInfo? sharedState = null)
    {
        Name = name;
        TypeName = typeName;
        TypeInfo = typeInfo;
        SharedState = sharedState ?? SharedStateInfo.NotShared;
    }

    public bool Equals(PropertyInfo? other)
    {
        if (other is null) return false;
        return Name == other.Name &&
               TypeName == other.TypeName &&
               Equals(TypeInfo, other.TypeInfo) &&
               Equals(SharedState, other.SharedState);
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyInfo);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (Name?.GetHashCode() ?? 0) * 397 ^ (TypeName?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (TypeInfo?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (SharedState?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// Groups class declaration modifiers to reduce positional parameter ambiguity.
/// </summary>
internal struct ClassModifiers : IEquatable<ClassModifiers>
{
    public bool IsPublic { get; }
    public bool IsRecord { get; }
    public bool IsPartial { get; }

    public ClassModifiers(bool isPublic, bool isRecord = false, bool isPartial = true)
    {
        IsPublic = isPublic;
        IsRecord = isRecord;
        IsPartial = isPartial;
    }

    public bool Equals(ClassModifiers other) =>
        IsPublic == other.IsPublic && IsRecord == other.IsRecord && IsPartial == other.IsPartial;
    public override bool Equals(object? obj) => obj is ClassModifiers m && Equals(m);
    public override int GetHashCode()
    {
        unchecked { return (IsPublic.GetHashCode() * 397 ^ IsRecord.GetHashCode()) * 397 ^ IsPartial.GetHashCode(); }
    }
}

/// <summary>
/// Groups inheritance-related fields for [State] classes.
/// </summary>
internal struct InheritanceInfo : IEquatable<InheritanceInfo>
{
    /// <summary>True if class inherits from State (directly or transitively).</summary>
    public bool InheritsFromState { get; }
    /// <summary>True if immediate base class is also a [State] class (for call-base pattern).</summary>
    public bool HasImmediateStateBase { get; }
    /// <summary>Full type name of immediate [State] base class, if any.</summary>
    public string? ImmediateStateBaseTypeName { get; }

    public InheritanceInfo(bool inheritsFromState, bool hasImmediateStateBase = false, string? immediateStateBaseTypeName = null)
    {
        InheritsFromState = inheritsFromState;
        HasImmediateStateBase = hasImmediateStateBase;
        ImmediateStateBaseTypeName = immediateStateBaseTypeName;
    }

    public bool Equals(InheritanceInfo other) =>
        InheritsFromState == other.InheritsFromState &&
        HasImmediateStateBase == other.HasImmediateStateBase &&
        ImmediateStateBaseTypeName == other.ImmediateStateBaseTypeName;
    public override bool Equals(object? obj) => obj is InheritanceInfo i && Equals(i);
    public override int GetHashCode()
    {
        unchecked
        {
            return (InheritsFromState.GetHashCode() * 397 ^ HasImmediateStateBase.GetHashCode()) * 397
                   ^ (ImmediateStateBaseTypeName?.GetHashCode() ?? 0);
        }
    }
}

/// <summary>
/// Information about a [State] class for code generation.
/// </summary>
internal sealed class StateClassInfo : IEquatable<StateClassInfo>
{
    public string ClassName { get; }
    public string? Namespace { get; }
    /// <summary>All properties (own + inherited) for hashing and freezing.</summary>
    public ImmutableArray<PropertyInfo> Properties { get; }
    /// <summary>Only own properties for cloning (call-base pattern).</summary>
    public ImmutableArray<PropertyInfo> OwnProperties { get; }
    public ClassModifiers Modifiers { get; }
    public InheritanceInfo Inheritance { get; }
    public ImmutableArray<TypeParameterInfo> TypeParameters { get; }
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
    public string FullTypeName { get; }
    public Location? ClassLocation { get; }

    public StateClassInfo(
        string className,
        string? ns,
        ImmutableArray<PropertyInfo> properties,
        ImmutableArray<PropertyInfo> ownProperties,
        ClassModifiers modifiers,
        InheritanceInfo inheritance = default,
        ImmutableArray<TypeParameterInfo> typeParameters = default,
        ImmutableArray<DiagnosticInfo> diagnostics = default,
        string? fullTypeName = null,
        Location? classLocation = null)
    {
        ClassName = className;
        Namespace = ns;
        Properties = properties;
        OwnProperties = ownProperties;
        Modifiers = modifiers;
        Inheritance = inheritance;
        TypeParameters = typeParameters.IsDefault ? ImmutableArray<TypeParameterInfo>.Empty : typeParameters;
        Diagnostics = diagnostics.IsDefault ? ImmutableArray<DiagnosticInfo>.Empty : diagnostics;
        FullTypeName = fullTypeName ?? (ns != null ? $"{ns}.{className}" : className);
        ClassLocation = classLocation;
    }

    public bool Equals(StateClassInfo? other)
    {
        if (other is null) return false;
        return FullTypeName == other.FullTypeName && Namespace == other.Namespace;
    }

    public override bool Equals(object? obj) => Equals(obj as StateClassInfo);
    public override int GetHashCode()
    {
        unchecked
        {
            return (FullTypeName?.GetHashCode() ?? 0) * 397 ^ (Namespace?.GetHashCode() ?? 0);
        }
    }
}
