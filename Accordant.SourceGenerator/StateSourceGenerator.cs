// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator;

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Incremental source generator that generates State implementations for POCO classes
/// marked with [State] attribute.
///
/// Pipeline overview (read the partial class files in this order):
///
///   1. This file — Pipeline wiring. Initialize sets up the Roslyn incremental
///      pipeline: syntax filter → semantic transform → execute.
///   2. StateSourceGenerator.Extraction.cs — Semantic analysis. Extracts
///      StateClassInfo from symbols: class validation, base-type checks,
///      property enumeration, SharedState handling.
///   3. StateSourceGenerator.TypeAnalysis.cs — Type classification. AnalyzeType
///      classifies each property type into a TypeKind (Primitive, String, List,
///      Dictionary, StateClass, etc.) and emits diagnostics for unsupported types.
///   4. StateSourceGenerator.Validation.cs — Error propagation. Uses fixed-point
///      iteration to transitively mark classes whose dependencies have errors,
///      then reports all diagnostics.
///   5. StateSourceGenerator.Emitter.cs — Code generation. Produces the partial
///      class with CloneInternal, AppendFieldHashes, StringRepresentationInternal,
///      and FreezeComponents. Delegates per-type logic to TypeKindHandlers.cs.
///
/// Supporting files (data models and helpers):
///
///   - StateGeneratorModels.cs — Data models: StateClassInfo, PropertyInfo,
///     TypeInfo, SharedStateInfo.
///   - TypeKindHandlers.cs — Strategy pattern: per-TypeKind handlers for
///     Clone/Hash/Fingerprint/Freeze code emission.
///   - StateDiagnostics.cs — Diagnostic descriptors (STATE001–STATE025).
///   - StateDiagnosticMapper.cs — Maps DiagnosticKind → DiagnosticDescriptor.
///   - StringEscaper.cs — Generates C# expressions for escaped strings in
///     fingerprint output.
/// </summary>
[Generator]
public partial class StateSourceGenerator : IIncrementalGenerator
{
    private const string StateAttributeFullName = "Microsoft.Accordant.StateAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all class declarations with [State] attribute
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateClass(node),
                transform: static (ctx, ct) => GetStateClassInfo(ctx, ct))
            .Where(static info => info is not null);

        // Combine with compilation
        var compilationAndClasses = context.CompilationProvider
            .Combine(classDeclarations.Collect());

        // Generate source
        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    /// <summary>
    /// Syntax-level fast filter — runs on every type declaration in the compilation.
    /// Returns true for any class/record/struct with attributes, so the semantic
    /// transform (<see cref="GetStateClassInfo"/>) can check for [State] properly.
    ///
    /// Records and structs are included deliberately: if someone writes
    /// <c>[State] public partial struct Foo</c>, we want to emit STATE004/STATE006
    /// rather than silently ignoring it.
    /// </summary>
    private static bool IsCandidateClass(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax classDecl)
        {
            return classDecl.AttributeLists.Count > 0;
        }
        if (node is RecordDeclarationSyntax recordDecl)
        {
            return recordDecl.AttributeLists.Count > 0;
        }
        if (node is StructDeclarationSyntax structDecl)
        {
            return structDecl.AttributeLists.Count > 0;
        }
        return false;
    }

    private static StateClassInfo? GetStateClassInfo(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var semanticModel = context.SemanticModel;
        TypeDeclarationSyntax typeDecl;
        SyntaxList<AttributeListSyntax> attributeLists;
        SyntaxTokenList modifiers;
        bool isRecord = false;
        bool isStruct = false;

        if (context.Node is ClassDeclarationSyntax classDecl)
        {
            typeDecl = classDecl;
            attributeLists = classDecl.AttributeLists;
            modifiers = classDecl.Modifiers;
        }
        else if (context.Node is RecordDeclarationSyntax recordDecl)
        {
            typeDecl = recordDecl;
            attributeLists = recordDecl.AttributeLists;
            modifiers = recordDecl.Modifiers;
            isRecord = true;
        }
        else if (context.Node is StructDeclarationSyntax structDecl)
        {
            typeDecl = structDecl;
            attributeLists = structDecl.AttributeLists;
            modifiers = structDecl.Modifiers;
            isStruct = true;
        }
        else
        {
            return null;
        }

        // Check for [State] attribute
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(attribute, cancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                {
                    var attributeType = methodSymbol.ContainingType;
                    // Check fully-qualified name to avoid matching unrelated StateAttribute classes
                    if (attributeType.ToDisplayString() == StateAttributeFullName)
                    {
                        // Structs are not supported - return minimal info to emit STATE004
                        if (isStruct)
                        {
                            var classLocation = typeDecl.GetLocation();
                            var structName = typeDecl.Identifier.Text;
                            var structIsPartial = modifiers.Any(SyntaxKind.PartialKeyword);
                            return new StateClassInfo(
                                structName,
                                null,
                                ImmutableArray<PropertyInfo>.Empty,
                                ImmutableArray<PropertyInfo>.Empty,
                                new ClassModifiers(isPublic: false, isRecord: false, isPartial: structIsPartial),
                                diagnostics: ImmutableArray.Create(DiagnosticInfo.Create(
                                    DiagnosticKind.StructNotSupported,
                                    structName,
                                    null,
                                    classLocation)),
                                fullTypeName: structName,
                                classLocation: classLocation);
                        }

                        var classSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                        if (classSymbol is INamedTypeSymbol namedType)
                        {
                            bool isPartial = modifiers.Any(SyntaxKind.PartialKeyword);
                            return ExtractClassInfo(namedType, typeDecl, isRecord, isPartial);
                        }
                    }
                }
            }
        }

        return null;
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<StateClassInfo?> classes,
        SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
            return;

        var distinctClasses = classes
            .Where(c => c is not null)
            .Cast<StateClassInfo>()
            .Distinct()
            .ToList();

        // Invariant check (Area 5): every TypeKind.StateClass TypeName should reference
        // a known StateClassInfo.FullTypeName. Mismatches indicate a naming inconsistency
        // between type analysis and class extraction.
        ValidateStateClassTypeNameConsistency(distinctClasses);

        // Pass 1: Identify all classes that have errors (needed to propagate errors to dependent classes)
        var classesWithErrors = CollectClassesWithErrors(distinctClasses);

        // Pass 2: Report diagnostics and generate code for valid classes
        foreach (var classInfo in distinctClasses)
        {
            if (ReportDiagnosticsAndCheckErrors(classInfo, classesWithErrors, context))
                continue;  // Skip generation for classes with errors

            var source = GenerateStateClass(classInfo);
            var hintName = classInfo.FullTypeName.Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
            context.AddSource($"{hintName}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }
}
