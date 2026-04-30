// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Text;
    using System.Threading;

    public partial class StateSourceGenerator
    {
        /// <summary>
        /// Checks if a property symbol is a public, settable instance property (not static, not read-only, not indexer).
        /// Used to identify properties that should be included in clone/hash/freeze operations.
        /// </summary>
        private static bool IsPublicSettableProperty(IPropertySymbol property)
        {
            return property.DeclaredAccessibility == Accessibility.Public &&
                   !property.IsStatic &&
                   !property.IsReadOnly &&
                   !property.IsIndexer &&
                   property.SetMethod != null;
        }

        private static StateClassInfo ExtractClassInfo(
            INamedTypeSymbol classSymbol,
            TypeDeclarationSyntax typeDecl,
            bool isRecord,
            bool isPartial)
        {
            var properties = new List<PropertyInfo>();
            var diagnostics = new List<DiagnosticInfo>();
            var classLocation = typeDecl.Identifier.GetLocation();

            // Check if class is nested (not allowed)
            if (classSymbol.ContainingType != null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.NestedStateClass,
                    classSymbol.Name,
                    classSymbol.ContainingType.Name,
                    classLocation));
            }

            // Check if class is abstract (not allowed - can't generate new ClassName())
            if (classSymbol.IsAbstract)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.AbstractStateClass,
                    classSymbol.Name,
                    "",
                    classLocation));
            }

            // Check for parameterless constructor (any accessibility - generated code is in same partial)
            if (!HasParameterlessConstructor(classSymbol))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.MissingParameterlessConstructor,
                    classSymbol.Name,
                    "",
                    classLocation));
            }

            // Check if class already inherits from State
            bool inheritsFromState = InheritsFromState(classSymbol);

            // Check for non-[State] State base classes
            // If we inherit from State but the immediate base is not [State] and not State itself, that's an error
            var nonStateBase = FindNonStateBaseClass(classSymbol);
            if (nonStateBase != null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.NonStateBaseClass,
                    classSymbol.Name,
                    nonStateBase.Name,
                    classLocation));
            }

            // Check for invalid base class (not object, State, or [State] class)
            var invalidBase = FindInvalidBaseClass(classSymbol);
            if (invalidBase != null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.InvalidBaseClass,
                    classSymbol.Name,
                    invalidBase.Name,
                    classLocation));
            }

            // Check for hidden base properties (properties using 'new' to hide base class property)
            CheckForHiddenBaseProperties(classSymbol, classLocation, diagnostics);

            // Check for getter-only mutable properties
            CheckForGetterOnlyMutableProperties(classSymbol, classLocation, diagnostics);

            // Check for instance fields (not allowed in State classes)
            CheckForInstanceFields(classSymbol, classLocation, diagnostics);

            // Note: Properties with non-public setters (internal/protected/private) are still
            // included in generation since generated code is in the same partial class and
            // can access them. No need to error on these.

            // Get ALL properties (own + inherited) for fingerprinting and locking
            var allProperties = new List<PropertyInfo>();
            foreach (var propertySymbol in GetAllStateProperties(classSymbol))
            {
                var (propInfo, propDiagnostics) = ExtractPropertyInfo(propertySymbol, classSymbol);
                allProperties.Add(propInfo);
                diagnostics.AddRange(propDiagnostics);
            }

            // Get OWN properties only for cloning (call-base pattern)
            var ownProperties = new List<PropertyInfo>();
            foreach (var propertySymbol in GetOwnStateProperties(classSymbol))
            {
                var (propInfo, _) = ExtractPropertyInfo(propertySymbol, classSymbol);  // Diagnostics already collected above
                ownProperties.Add(propInfo);
            }

            // Check if immediate base class has [State] attribute (for call-base pattern)
            bool hasImmediateStateBase = classSymbol.BaseType != null &&
                classSymbol.BaseType.GetAttributes().Any(IsAccordantStateAttribute);

            // Get the full type name of the [State] base class, if any
            string? immediateStateBaseTypeName = null;
            if (hasImmediateStateBase && classSymbol.BaseType != null)
            {
                var baseNs = classSymbol.BaseType.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : classSymbol.BaseType.ContainingNamespace.ToDisplayString();
                immediateStateBaseTypeName = baseNs != null
                    ? $"{baseNs}.{classSymbol.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
                    : classSymbol.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }

            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            // Get generic type parameters with full constraint info
            var typeParameters = classSymbol.TypeParameters
                .Select(tp => ExtractTypeParameterInfo(tp))
                .ToImmutableArray();

            bool isPublic = typeDecl.Modifiers.Any(SyntaxKind.PublicKeyword);

            // Build fully qualified name using shared helper for consistency with TypeInfo lookups
            var fullTypeName = GetFullTypeName(classSymbol);

            return new StateClassInfo(
                className: classSymbol.Name,
                ns: namespaceName,
                properties: allProperties.ToImmutableArray(),
                ownProperties: ownProperties.ToImmutableArray(),
                modifiers: new ClassModifiers(isPublic, isRecord, isPartial),
                inheritance: new InheritanceInfo(inheritsFromState, hasImmediateStateBase, immediateStateBaseTypeName),
                typeParameters: typeParameters,
                diagnostics: diagnostics.ToImmutableArray(),
                fullTypeName: fullTypeName,
                classLocation: classLocation);
        }

        /// <summary>
        /// Extracts full type parameter information including all constraint kinds.
        /// </summary>
        private static TypeParameterInfo ExtractTypeParameterInfo(ITypeParameterSymbol tp)
        {
            var constraints = new List<string>();

            // Special constraints must come first in a specific order
            if (tp.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (tp.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (tp.HasReferenceTypeConstraint)
                constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (tp.HasNotNullConstraint)
                constraints.Add("notnull");

            // Type constraints
            foreach (var constraintType in tp.ConstraintTypes)
            {
                constraints.Add(constraintType.ToDisplayString());
            }

            // new() must come last
            if (tp.HasConstructorConstraint)
                constraints.Add("new()");

            return new TypeParameterInfo(tp.Name, constraints.ToImmutableArray());
        }

        /// <summary>
        /// Checks if a class has a parameterless constructor (any accessibility).
        /// Generated code is in the same partial class, so even private ctors work.
        /// </summary>
        private static bool HasParameterlessConstructor(INamedTypeSymbol classSymbol)
        {
            var constructors = classSymbol.InstanceConstructors;

            // If no constructors are explicitly defined, compiler generates a default parameterless one
            if (constructors.All(c => c.IsImplicitlyDeclared))
                return true;

            // Check for any parameterless constructor (accessibility doesn't matter for partial class)
            return constructors.Any(c => c.Parameters.Length == 0);
        }

        #region Base Type Helpers

        /// <summary>
        /// Enumerates all base types from immediate base to object.
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(INamedTypeSymbol classSymbol)
        {
            var baseType = classSymbol.BaseType;
            while (baseType != null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }
        }

        /// <summary>
        /// Checks if a type is the Accordant.State base class.
        /// </summary>
        private static bool IsAccordantStateClass(INamedTypeSymbol type)
        {
            return type.Name == "State" && 
                   type.ContainingNamespace?.ToDisplayString() == "Microsoft.Accordant";
        }

        /// <summary>
        /// Checks if a type has the [State] attribute.
        /// </summary>
        private static bool HasStateAttribute(INamedTypeSymbol type)
        {
            return type.GetAttributes().Any(IsAccordantStateAttribute);
        }

        /// <summary>
        /// Checks if a class already inherits from State or a [State]-attributed class.
        /// </summary>
        private static bool InheritsFromState(INamedTypeSymbol classSymbol)
        {
            return EnumerateBaseTypes(classSymbol).Any(bt => 
                IsAccordantStateClass(bt) || HasStateAttribute(bt));
        }

        #endregion

        /// <summary>
        /// Finds a base class that inherits from State but doesn't have [State] attribute.
        /// Returns null if no such problematic base exists.
        /// </summary>
        private static INamedTypeSymbol? FindNonStateBaseClass(INamedTypeSymbol classSymbol)
        {
            foreach (var baseType in EnumerateBaseTypes(classSymbol))
            {
                // If we hit the Accordant.State base class, we're good
                if (IsAccordantStateClass(baseType))
                    return null;

                // If base inherits from State (directly or indirectly) but doesn't have [State], that's a problem
                if (!HasStateAttribute(baseType) && InheritsFromState(baseType))
                    return baseType;
            }
            return null;
        }

        /// <summary>
        /// Finds the immediate base class if it's invalid (not object, State, or a [State] class).
        /// Returns null if the base class is valid.
        /// </summary>
        private static INamedTypeSymbol? FindInvalidBaseClass(INamedTypeSymbol classSymbol)
        {
            var baseType = classSymbol.BaseType;
            
            // No explicit base or inheriting from object is valid
            if (baseType == null || baseType.SpecialType == SpecialType.System_Object)
                return null;

            // Inheriting from Accordant.State is valid
            if (IsAccordantStateClass(baseType))
                return null;

            // Inheriting from a [State] class is valid
            if (HasStateAttribute(baseType))
                return null;

            // Inheriting from something that inherits from State is handled by FindNonStateBaseClass (STATE013)
            // This check is for completely unrelated base classes
            if (InheritsFromState(baseType))
                return null;  // Will be caught by FindNonStateBaseClass

            // Invalid base class - not object, State, or [State]
            return baseType;
        }

        /// <summary>
        /// Checks if any property in the class hides a base class property from a [State] class.
        /// </summary>
        private static void CheckForHiddenBaseProperties(
            INamedTypeSymbol classSymbol,
            Location classLocation,
            List<DiagnosticInfo> diagnostics)
        {
            // Get all property names from [State] base classes
            var basePropertyNames = new HashSet<string>();
            foreach (var baseType in EnumerateBaseTypes(classSymbol))
            {
                if (HasStateAttribute(baseType))
                {
                    foreach (var member in baseType.GetMembers())
                    {
                        if (member is IPropertySymbol prop &&
                            prop.DeclaredAccessibility == Accessibility.Public &&
                            !prop.IsStatic &&
                            prop.SetMethod != null)
                        {
                            basePropertyNames.Add(prop.Name);
                        }
                    }
                }
            }

            // Check if any property in this class hides a base property (but not overrides)
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol prop &&
                    prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    prop.SetMethod != null &&
                    !prop.IsOverride &&  // Override is valid, hiding with 'new' is not
                    basePropertyNames.Contains(prop.Name))
                {
                    // This property has the same name as a base [State] property - it's hiding it
                    var propLocation = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                        ?? classLocation;
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticKind.HiddenBaseProperty,
                        prop.Name,
                        classSymbol.Name,
                        propLocation));
                }
            }
        }

        /// <summary>
        /// Checks for getter-only properties that have mutable types (List, Dictionary, Array, State).
        /// These are dangerous because the contents can be mutated but can't be cloned properly.
        /// </summary>
        private static void CheckForGetterOnlyMutableProperties(
            INamedTypeSymbol classSymbol,
            Location classLocation,
            List<DiagnosticInfo> diagnostics)
        {
            // Check all own public properties that don't have setters
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol prop &&
                    prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    prop.SetMethod == null)  // No setter
                {
                    // Analyze the type to see if it's mutable
                    var (typeInfo, _) = AnalyzeType(prop.Type, prop.Name, null);
                    if (IsMutableType(typeInfo))
                    {
                        var propLocation = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                            ?? classLocation;
                        diagnostics.Add(DiagnosticInfo.GetterOnlyMutableProperty(
                            prop.Name,
                            prop.Type.ToDisplayString(),
                            propLocation,
                            classSymbol.Name));
                    }
                }
            }
        }

        /// <summary>
        /// Checks for instance fields in the State class.
        /// Instance fields are not allowed because they won't be cloned, fingerprinted, or locked.
        /// Only const and static fields are permitted.
        /// </summary>
        private static void CheckForInstanceFields(
            INamedTypeSymbol classSymbol,
            Location classLocation,
            List<DiagnosticInfo> diagnostics)
        {
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IFieldSymbol field &&
                    !field.IsConst &&
                    !field.IsStatic &&
                    !field.IsImplicitlyDeclared)  // Skip compiler-generated backing fields
                {
                    var fieldLocation = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                        ?? classLocation;
                    diagnostics.Add(DiagnosticInfo.InstanceFieldNotAllowed(
                        field.Name,
                        classSymbol.Name,
                        fieldLocation));
                }
            }
        }

        /// <summary>
        /// Gets public settable properties declared directly in this class (not inherited).
        /// Excludes override properties since base.CloneInternal() already handles them.
        /// </summary>
        private static IEnumerable<IPropertySymbol> GetOwnStateProperties(INamedTypeSymbol classSymbol)
        {
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol propertySymbol &&
                    IsPublicSettableProperty(propertySymbol) &&
                    !propertySymbol.IsOverride)  // Base handles overrides via virtual dispatch
                {
                    yield return propertySymbol;
                }
            }
        }

        /// <summary>
        /// Gets all public settable properties including inherited ones from [State] base classes.
        /// Used for hashing and freezing where we need the complete picture.
        /// </summary>
        private static IEnumerable<IPropertySymbol> GetAllStateProperties(INamedTypeSymbol classSymbol)
        {
            var seenProperties = new HashSet<string>();

            // First get properties from this class
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol propertySymbol &&
                    IsPublicSettableProperty(propertySymbol))
                {
                    seenProperties.Add(propertySymbol.Name);
                    yield return propertySymbol;
                }
            }

            // Then get inherited properties from [State] base classes
            foreach (var baseType in EnumerateBaseTypes(classSymbol))
            {
                if (HasStateAttribute(baseType))
                {
                    foreach (var member in baseType.GetMembers())
                    {
                        if (member is IPropertySymbol propertySymbol &&
                            IsPublicSettableProperty(propertySymbol) &&
                            !seenProperties.Contains(propertySymbol.Name))
                        {
                            seenProperties.Add(propertySymbol.Name);
                            yield return propertySymbol;
                        }
                    }
                }
            }
        }

        private static (PropertyInfo, List<DiagnosticInfo>) ExtractPropertyInfo(IPropertySymbol propertySymbol, INamedTypeSymbol containingClass)
        {
            var diagnostics = new List<DiagnosticInfo>();
            // Use DeclaringSyntaxReferences to get the actual syntax location (works better in VS)
            var propertyLocation = propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                ?? propertySymbol.Locations.FirstOrDefault();

            // Check for init-only setter
            if (propertySymbol.SetMethod?.IsInitOnly == true)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.InitOnlyProperty,
                    propertySymbol.Name,
                    propertySymbol.Type.ToDisplayString(),
                    propertyLocation));
            }

            // Check for required modifier
            if (propertySymbol.IsRequired)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.RequiredProperty,
                    propertySymbol.Name,
                    propertySymbol.Type.ToDisplayString(),
                    propertyLocation));
            }

            var (typeInfo, typeDiagnostics) = AnalyzeType(propertySymbol.Type, propertySymbol.Name, propertyLocation);
            diagnostics.AddRange(typeDiagnostics);

            // Check for [SharedState] attribute (needs typeInfo to determine if fingerprint is required)
            var (sharedState, sharedStateDiagnostics) = 
                ExtractSharedStateInfo(propertySymbol, containingClass, propertyLocation, typeInfo);
            diagnostics.AddRange(sharedStateDiagnostics);

            return (new PropertyInfo(
                propertySymbol.Name,
                propertySymbol.Type.ToDisplayString(),
                typeInfo,
                sharedState), diagnostics);
        }

        private const string SharedStateAttributeFullName = "Microsoft.Accordant.SharedStateAttribute";

        private static (SharedStateInfo sharedState, List<DiagnosticInfo> diagnostics) ExtractSharedStateInfo(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol containingClass,
            Location? propertyLocation,
            TypeInfo typeInfo)
        {
            var diagnostics = new List<DiagnosticInfo>();
            
            // Find [SharedState] attribute
            var sharedStateAttr = propertySymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == SharedStateAttributeFullName);
            
            if (sharedStateAttr == null)
            {
                return (SharedStateInfo.NotShared, diagnostics);
            }

            // Check for value type - SharedState doesn't make sense for value types
            if (propertySymbol.Type.IsValueType)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.SharedStateOnValueType,
                    propertySymbol.Name,
                    propertySymbol.Type.ToDisplayString(),
                    propertyLocation));
                return (SharedStateInfo.SharedNoFingerprint(), diagnostics);
            }

            // Get Fingerprint property value if specified
            string? fingerprintMethod = null;
            foreach (var namedArg in sharedStateAttr.NamedArguments)
            {
                if (namedArg.Key == "Fingerprint" && namedArg.Value.Value is string methodName)
                {
                    fingerprintMethod = methodName;
                }
            }

            // Check if property type is a [State] class
            bool isStateType = IsStateClass(propertySymbol.Type);

            // Fingerprint method is only required for types we can't hash natively
            if (!isStateType && !typeInfo.CanBeHashedNatively && fingerprintMethod == null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticKind.SharedStateRequiresFingerprint,
                    propertySymbol.Name,
                    propertySymbol.Type.ToDisplayString(),
                    propertyLocation));
                return (SharedStateInfo.SharedNoFingerprint(), diagnostics);
            }

            // No custom fingerprint specified
            if (fingerprintMethod == null)
            {
                return (SharedStateInfo.SharedNoFingerprint(), diagnostics);
            }

            // Validate and classify the fingerprint member
            var (isMethod, validationDiagnostics) = ValidateFingerprintMethod(
                propertySymbol, containingClass, fingerprintMethod, propertyLocation);
            diagnostics.AddRange(validationDiagnostics);

            var sharedState = isMethod
                ? SharedStateInfo.SharedWithMethod(fingerprintMethod)
                : SharedStateInfo.SharedWithProperty(fingerprintMethod);

            return (sharedState, diagnostics);
        }

        /// <summary>
        /// Validates the fingerprint method/property for a [SharedState] attribute.
        /// Returns (isMethod, diagnostics) where isMethod indicates if it's a method (not a property).
        /// </summary>
        private static (bool isMethod, List<DiagnosticInfo> diagnostics) ValidateFingerprintMethod(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol containingClass,
            string fingerprintMethod,
            Location? propertyLocation)
        {
            var diagnostics = new List<DiagnosticInfo>();
            var propertyType = propertySymbol.Type;

            // First check if it's a string property (valid fingerprint)
            // Look in class and all base classes
            var fingerprintProperty = GetAllMembers(containingClass, fingerprintMethod)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.GetMethod != null);
            
            if (fingerprintProperty != null)
            {
                if (fingerprintProperty.Type.SpecialType == SpecialType.System_String)
                {
                    // Valid: it's a string property (like ContentFingerprint)
                    return (false, diagnostics); // isMethod = false
                }
                else
                {
                    // Property found but doesn't return string
                    diagnostics.Add(DiagnosticInfo.FingerprintWrongSignature(
                        propertySymbol.Name,
                        propertySymbol.Type.ToDisplayString(),
                        propertyLocation,
                        fingerprintMethod));
                    return (false, diagnostics);
                }
            }

            // Check for method with correct signature: string MethodName(PropertyType value)
            // Look in class and all base classes
            var fingerprintMethods = GetAllMembers(containingClass, fingerprintMethod)
                .OfType<IMethodSymbol>()
                .ToList();

            if (fingerprintMethods.Count == 0)
            {
                diagnostics.Add(DiagnosticInfo.FingerprintNotFound(
                    propertySymbol.Name,
                    fingerprintMethod,
                    propertyLocation,
                    containingClass.Name));
                return (false, diagnostics);
            }

            // Check if any method has correct signature
            var validMethod = fingerprintMethods.FirstOrDefault(m =>
                m.ReturnType.SpecialType == SpecialType.System_String &&
                m.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, propertyType));

            if (validMethod == null)
            {
                diagnostics.Add(DiagnosticInfo.FingerprintWrongSignature(
                    propertySymbol.Name,
                    propertySymbol.Type.ToDisplayString(),
                    propertyLocation,
                    fingerprintMethod));
                return (false, diagnostics);
            }

            return (true, diagnostics); // isMethod = true
        }

        /// <summary>
        /// Gets all members with the specified name from the class and its base classes.
        /// This is needed because INamedTypeSymbol.GetMembers() only returns directly declared members.
        /// </summary>
        private static IEnumerable<ISymbol> GetAllMembers(INamedTypeSymbol? type, string name)
        {
            while (type != null)
            {
                foreach (var member in type.GetMembers(name))
                {
                    yield return member;
                }
                type = type.BaseType;
            }
        }
    }
}
