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
        /// Validates that every property with TypeKind.StateClass references a known StateClassInfo.
        /// This catches naming inconsistencies between AnalyzeType (which sets TypeInfo.TypeName)
        /// and ExtractClassInfo (which sets StateClassInfo.FullTypeName).
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void ValidateStateClassTypeNameConsistency(List<StateClassInfo> distinctClasses)
        {
            var knownFullTypeNames = new HashSet<string>(distinctClasses.Select(c => c.FullTypeName));

            foreach (var classInfo in distinctClasses)
            {
                foreach (var prop in classInfo.Properties)
                {
                    ValidateTypeInfoReferences(prop.TypeInfo, classInfo.FullTypeName, prop.Name, knownFullTypeNames);
                }
            }
        }

        private static void ValidateTypeInfoReferences(TypeInfo typeInfo, string owningClass, string propertyName, HashSet<string> knownNames)
        {
            // Only validate concrete StateClass references with namespace-qualified names.
            // Skip generic type parameters (e.g., "T") which are constrained to State
            // but aren't actual class names — they have no namespace qualifier.
            if (typeInfo.Kind == TypeKind.StateClass
                && typeInfo.TypeName.Contains(".")
                && !knownNames.Contains(typeInfo.TypeName))
            {
                System.Diagnostics.Debug.Fail(
                    $"TypeInfo.TypeName '{typeInfo.TypeName}' for property '{propertyName}' " +
                    $"in class '{owningClass}' does not match any known StateClassInfo.FullTypeName. " +
                    $"Known names: [{string.Join(", ", knownNames)}]");
            }

            // Recurse into inner types (Nullable<StateClass>, List<StateClass>, Dictionary<K, StateClass>, etc.)
            if (typeInfo.InnerType != null)
                ValidateTypeInfoReferences(typeInfo.InnerType, owningClass, propertyName, knownNames);
            if (typeInfo.SecondaryType != null)
                ValidateTypeInfoReferences(typeInfo.SecondaryType, owningClass, propertyName, knownNames);
            if (typeInfo.TupleElements.HasValue && typeInfo.TupleElements.Value.Length > 0)
            {
                foreach (var elem in typeInfo.TupleElements.Value)
                    ValidateTypeInfoReferences(elem, owningClass, propertyName, knownNames);
            }
        }
        /// Uses fixed-point iteration to propagate errors transitively through dependencies.
        /// </summary>
        private static HashSet<string> CollectClassesWithErrors(List<StateClassInfo> distinctClasses)
        {
            var classesWithErrors = new HashSet<string>();
            
            // Seed with classes that have direct errors
            foreach (var classInfo in distinctClasses)
            {
                if (HasClassErrors(classInfo))
                {
                    classesWithErrors.Add(classInfo.FullTypeName);
                }
            }

            // Fixed-point iteration: propagate errors transitively
            // Keep adding classes that reference errored classes until no new ones found
            bool changed;
            do
            {
                changed = false;
                foreach (var classInfo in distinctClasses)
                {
                    if (classesWithErrors.Contains(classInfo.FullTypeName))
                        continue;  // Already in error set

                    // Check if base class has errors
                    if (classInfo.Inheritance.ImmediateStateBaseTypeName != null && 
                        classesWithErrors.Contains(classInfo.Inheritance.ImmediateStateBaseTypeName))
                    {
                        classesWithErrors.Add(classInfo.FullTypeName);
                        changed = true;
                        continue;
                    }

                    // Check if any property references an errored class
                    foreach (var prop in classInfo.Properties)
                    {
                        if (FindStateClassWithErrors(prop.TypeInfo, classesWithErrors) != null)
                        {
                            classesWithErrors.Add(classInfo.FullTypeName);
                            changed = true;
                            break;
                        }
                    }
                }
            } while (changed);

            return classesWithErrors;
        }

        /// <summary>
        /// Pass 2: Reports all diagnostics for a class and returns true if generation should be skipped.
        /// </summary>
        private static bool ReportDiagnosticsAndCheckErrors(
            StateClassInfo classInfo,
            HashSet<string> classesWithErrors,
            SourceProductionContext context)
        {
            bool hasErrors = false;

            // Check for records (not supported - records cannot inherit from State class)
            if (classInfo.Modifiers.IsRecord)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StateDiagnostics.StateOnRecord,
                    classInfo.ClassLocation ?? Location.None,
                    classInfo.ClassName));
                hasErrors = true;
            }

            // Check for missing partial keyword
            if (!classInfo.Modifiers.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StateDiagnostics.MissingPartialKeyword,
                    classInfo.ClassLocation ?? Location.None,
                    classInfo.ClassName));
                hasErrors = true;
            }

            // Report property-level diagnostics
            foreach (var diag in classInfo.Diagnostics)
            {
                if (StateDiagnosticMapper.ReportDiagnostic(context, diag))
                    hasErrors = true;
            }

            // Check if base [State] class has errors
            if (classInfo.Inheritance.ImmediateStateBaseTypeName != null && classesWithErrors.Contains(classInfo.Inheritance.ImmediateStateBaseTypeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StateDiagnostics.BaseTypeHasErrors,
                    classInfo.ClassLocation ?? Location.None,
                    classInfo.Inheritance.ImmediateStateBaseTypeName));
                hasErrors = true;
            }

            // Check if any property references a [State] class that has errors
            foreach (var prop in classInfo.Properties)
            {
                var errorTypeRef = FindStateClassWithErrors(prop.TypeInfo, classesWithErrors);
                if (errorTypeRef != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        StateDiagnostics.PropertyTypeHasErrors,
                        classInfo.ClassLocation ?? Location.None,
                        prop.Name,
                        errorTypeRef));
                    hasErrors = true;
                }
            }

            return hasErrors;
        }

        /// <summary>
        /// Checks if a class has any errors that would prevent successful generation.
        /// </summary>
        private static bool HasClassErrors(StateClassInfo classInfo)
        {
            if (classInfo.Modifiers.IsRecord || !classInfo.Modifiers.IsPartial)
                return true;

            return classInfo.Diagnostics.Any(d => StateDiagnosticMapper.IsError(d.Kind));
        }

        /// <summary>
        /// Recursively searches a TypeInfo for any StateClass references that are in the error set.
        /// Returns the first error class found, or null if none.
        /// </summary>
        private static string? FindStateClassWithErrors(TypeInfo typeInfo, HashSet<string> classesWithErrors)
        {
            switch (typeInfo.Kind)
            {
                case TypeKind.StateClass:
                    if (classesWithErrors.Contains(typeInfo.TypeName))
                        return typeInfo.TypeName;
                    return null;

                case TypeKind.Nullable:
                    if (typeInfo.InnerType != null)
                        return FindStateClassWithErrors(typeInfo.InnerType, classesWithErrors);
                    return null;

                case TypeKind.List:
                case TypeKind.Array:
                    if (typeInfo.InnerType != null)
                        return FindStateClassWithErrors(typeInfo.InnerType, classesWithErrors);
                    return null;

                case TypeKind.Dictionary:
                    // Check both key and value types
                    if (typeInfo.InnerType != null)
                    {
                        var keyError = FindStateClassWithErrors(typeInfo.InnerType, classesWithErrors);
                        if (keyError != null) return keyError;
                    }
                    if (typeInfo.SecondaryType != null)
                    {
                        var valueError = FindStateClassWithErrors(typeInfo.SecondaryType, classesWithErrors);
                        if (valueError != null) return valueError;
                    }
                    return null;

                case TypeKind.ValueTuple:
                    if (typeInfo.TupleElements != null)
                    {
                        foreach (var elem in typeInfo.TupleElements.Value)
                        {
                            var elemError = FindStateClassWithErrors(elem, classesWithErrors);
                            if (elemError != null) return elemError;
                        }
                    }
                    return null;

                default:
                    // TypeKind.Primitive, TypeKind.String, TypeKind.Enum — cannot contain State classes
                    return null;
            }
        }
    }
}
