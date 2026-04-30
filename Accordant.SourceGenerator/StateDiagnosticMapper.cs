// Copyright (c) Accordant. All rights reserved.
// Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.Accordant.SourceGenerator
{
    /// <summary>
    /// Centralizes mapping from DiagnosticKind to DiagnosticDescriptor and diagnostic reporting.
    /// Single source of truth for diagnostic metadata.
    /// 
    /// Note: STATE018 (PropertyTypeHasErrors) and STATE019 (BaseTypeHasErrors) are NOT handled here.
    /// They're "propagation" diagnostics computed dynamically during Execute() based on which classes
    /// have errors - they can't be determined during property extraction.
    /// </summary>
    internal static class StateDiagnosticMapper
    {
        /// <summary>
        /// Gets the DiagnosticDescriptor for a given DiagnosticKind.
        /// Returns null for unknown kinds.
        /// </summary>
        public static DiagnosticDescriptor? GetDescriptor(DiagnosticKind kind)
        {
            return kind switch
            {
                DiagnosticKind.InterfaceCollectionType => StateDiagnostics.InterfaceCollectionType,
                DiagnosticKind.UnconstrainedGenericType => StateDiagnostics.UnconstrainedGenericType,
                DiagnosticKind.UnsupportedExternalType => StateDiagnostics.UnsupportedExternalType,
                DiagnosticKind.NestedStateClass => StateDiagnostics.NestedStateClass,
                DiagnosticKind.MissingParameterlessConstructor => StateDiagnostics.MissingParameterlessConstructor,
                DiagnosticKind.UnsupportedDictionaryKeyType => StateDiagnostics.UnsupportedDictionaryKeyType,
                DiagnosticKind.InitOnlyProperty => StateDiagnostics.InitOnlyProperty,
                DiagnosticKind.RequiredProperty => StateDiagnostics.RequiredProperty,
                DiagnosticKind.MultidimensionalArray => StateDiagnostics.MultidimensionalArray,
                DiagnosticKind.NonStateBaseClass => StateDiagnostics.NonStateBaseClass,
                DiagnosticKind.HiddenBaseProperty => StateDiagnostics.HiddenBaseProperty,
                DiagnosticKind.AbstractStateClass => StateDiagnostics.AbstractStateClass,
                DiagnosticKind.GetterOnlyMutableProperty => StateDiagnostics.GetterOnlyMutableProperty,
                DiagnosticKind.InstanceFieldNotAllowed => StateDiagnostics.InstanceFieldNotAllowed,
                DiagnosticKind.StructNotSupported => StateDiagnostics.StructNotSupported,
                DiagnosticKind.InvalidBaseClass => StateDiagnostics.InvalidBaseClass,
                DiagnosticKind.SharedStateRequiresFingerprint => StateDiagnostics.SharedStateRequiresFingerprint,
                DiagnosticKind.SharedStateFingerprintNotFound => StateDiagnostics.SharedStateFingerprintNotFound,
                DiagnosticKind.SharedStateFingerprintWrongSignature => StateDiagnostics.SharedStateFingerprintWrongSignature,
                DiagnosticKind.SharedStateOnValueType => StateDiagnostics.SharedStateOnValueType,
                _ => null
            };
        }

        /// <summary>
        /// Checks if a DiagnosticKind represents an error-level diagnostic.
        /// </summary>
        public static bool IsError(DiagnosticKind kind)
        {
            var descriptor = GetDescriptor(kind);
            return descriptor?.DefaultSeverity == DiagnosticSeverity.Error;
        }

        /// <summary>
        /// Creates a Diagnostic from a DiagnosticInfo.
        /// Returns null if the DiagnosticKind is unknown.
        /// </summary>
        public static Diagnostic? CreateDiagnostic(DiagnosticInfo info)
        {
            var descriptor = GetDescriptor(info.Kind);
            if (descriptor == null)
                return null;

            var location = info.Location ?? Location.None;

            // Some diagnostics need additional arguments (ContextArg for STATE016, STATE017, etc.)
            if (NeedsContextArg(info.Kind) && info.ContextArg != null)
            {
                return Diagnostic.Create(
                    descriptor,
                    location,
                    info.SymbolName,
                    info.TypeName,
                    info.ContextArg);
            }

            return Diagnostic.Create(
                descriptor,
                location,
                info.SymbolName,
                info.TypeName);
        }

        /// <summary>
        /// Reports a diagnostic to the context from a DiagnosticInfo.
        /// Returns true if the diagnostic was an error.
        /// </summary>
        public static bool ReportDiagnostic(SourceProductionContext context, DiagnosticInfo info)
        {
            var diagnostic = CreateDiagnostic(info);
            if (diagnostic == null)
                return false;

            context.ReportDiagnostic(diagnostic);
            return IsError(info.Kind);
        }

        /// <summary>
        /// Checks if a DiagnosticKind requires the ContextArg in addition to SymbolName/TypeName.
        /// </summary>
        private static bool NeedsContextArg(DiagnosticKind kind)
        {
            return kind == DiagnosticKind.GetterOnlyMutableProperty ||
                   kind == DiagnosticKind.InstanceFieldNotAllowed ||
                   kind == DiagnosticKind.SharedStateFingerprintNotFound ||
                   kind == DiagnosticKind.SharedStateFingerprintWrongSignature;
        }
    }
}
