// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Enforces the statically knowable subset of the experimental structured
/// ProcessSystemModel workflow contract.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProcessWorkflowAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            ProcessWorkflowDiagnostics.DirectModelTaskAwait,
            ProcessWorkflowDiagnostics.ForeignAwait,
            ProcessWorkflowDiagnostics.RawCheckpointLoop,
            ProcessWorkflowDiagnostics.UnsupportedScalarType);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        WorkflowSymbols? symbols = WorkflowSymbols.Create(context.Compilation);
        if (symbols == null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, symbols),
            OperationKind.Invocation);
        context.RegisterOperationAction(
            operationContext =>
            {
                if (IsInsideModelTaskWorkflow(operationContext, symbols))
                {
                    AnalyzeAwait(operationContext, symbols);
                }
            },
            OperationKind.Await);
        context.RegisterOperationAction(
            operationContext =>
            {
                if (IsInsideModelTaskWorkflow(operationContext, symbols))
                {
                    AnalyzeLoop(operationContext, symbols);
                }
            },
            OperationKind.Loop);
    }

    private static bool IsInsideModelTaskWorkflow(
        OperationAnalysisContext context,
        WorkflowSymbols symbols)
    {
        for (IOperation? current = context.Operation.Parent;
            current != null;
            current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation anonymousFunction)
            {
                return symbols.IsModelTask(anonymousFunction.Symbol.ReturnType);
            }

            if (current is ILocalFunctionOperation localFunction)
            {
                return symbols.IsModelTask(localFunction.Symbol.ReturnType);
            }
        }

        return context.ContainingSymbol is IMethodSymbol method &&
            symbols.IsModelTask(method.ReturnType);
    }

    private static void AnalyzeAwait(
        OperationAnalysisContext context,
        WorkflowSymbols symbols)
    {
        var awaitOperation = (IAwaitOperation)context.Operation;
        ITypeSymbol? awaitedType = awaitOperation.Operation.Type;
        if (awaitedType == null || awaitedType is IErrorTypeSymbol)
        {
            return;
        }

        Location location = awaitOperation.Syntax is AwaitExpressionSyntax awaitSyntax
            ? awaitSyntax.AwaitKeyword.GetLocation()
            : awaitOperation.Syntax.GetLocation();
        string typeName = awaitedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (symbols.IsModelTask(awaitedType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ProcessWorkflowDiagnostics.DirectModelTaskAwait,
                location,
                typeName));
            return;
        }

        if (!symbols.IsModelAwaitable(awaitedType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ProcessWorkflowDiagnostics.ForeignAwait,
                location,
                typeName));
        }
    }

    private static void AnalyzeLoop(
        OperationAnalysisContext context,
        WorkflowSymbols symbols)
    {
        var loop = (ILoopOperation)context.Operation;
        if (!ContainsModelCheckpoint(loop, symbols))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProcessWorkflowDiagnostics.RawCheckpointLoop,
            loop.Syntax.GetLocation(),
            LoopKind(loop.Syntax)));
    }

    private static bool ContainsModelCheckpoint(
        IOperation operation,
        WorkflowSymbols symbols)
    {
        var pending = new Stack<IOperation>();
        foreach (IOperation child in operation.ChildOperations)
        {
            pending.Push(child);
        }

        while (pending.Count != 0)
        {
            IOperation current = pending.Pop();
            if (current is IAnonymousFunctionOperation ||
                current is ILocalFunctionOperation)
            {
                continue;
            }

            if (current is IInvocationOperation invocation &&
                symbols.IsModelContextCheckpoint(invocation.TargetMethod))
            {
                return true;
            }

            if (current is IAwaitOperation awaitOperation &&
                symbols.IsModelAwaitable(awaitOperation.Operation.Type))
            {
                return true;
            }

            foreach (IOperation child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return false;
    }

    private static string LoopKind(SyntaxNode syntax)
    {
        if (syntax is WhileStatementSyntax)
        {
            return "while";
        }

        if (syntax is ForStatementSyntax)
        {
            return "for";
        }

        if (syntax is ForEachStatementSyntax ||
            syntax is ForEachVariableStatementSyntax)
        {
            return "foreach";
        }

        if (syntax is DoStatementSyntax)
        {
            return "do";
        }

        return "language";
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        WorkflowSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;

        if (symbols.IsModelContextMethod(method))
        {
            AnalyzeModelContextInvocation(context, invocation, symbols);
            return;
        }

        if (symbols.IsProcessRegistrationMethod(method))
        {
            AnalyzeSubject(context, invocation, method.Name, symbols);
        }
    }

    private static void AnalyzeModelContextInvocation(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        WorkflowSymbols symbols)
    {
        IMethodSymbol method = invocation.TargetMethod;
        if (symbols.HasScalarCheckpointResult(method) &&
            invocation.Type is INamedTypeSymbol resultType &&
            symbols.IsModelAwaitable(resultType))
        {
            ReportUnsupportedType(
                context,
                resultType.TypeArguments[0],
                invocation.Syntax.GetLocation(),
                method.Name + " result",
                symbols);
        }

        IArgumentOperation? frameArgument = FindArgument(invocation, "argument");
        if (frameArgument != null)
        {
            ReportUnsupportedValue(
                context,
                frameArgument.Value,
                method.Name + " argument",
                symbols);
        }

        AnalyzeSubject(context, invocation, method.Name, symbols);
    }

    private static void AnalyzeSubject(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        string methodName,
        WorkflowSymbols symbols)
    {
        IArgumentOperation? subject = FindArgument(invocation, "subject");
        if (subject == null || subject.IsImplicit)
        {
            return;
        }

        UnsupportedValue? unsupported =
            subject.Parameter?.Type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Delegate
                ? FindUnsupportedDelegateResult(subject.Value, symbols)
                : FindUnsupportedValue(subject.Value, symbols);
        if (unsupported != null)
        {
            ReportUnsupportedType(
                context,
                unsupported.Type,
                unsupported.Location,
                methodName + " subject",
                symbols);
        }
    }

    private static IArgumentOperation? FindArgument(
        IInvocationOperation invocation,
        string parameterName)
    {
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == parameterName)
            {
                return argument;
            }
        }

        return null;
    }

    private static void ReportUnsupportedValue(
        OperationAnalysisContext context,
        IOperation value,
        string use,
        WorkflowSymbols symbols)
    {
        UnsupportedValue? unsupported = FindUnsupportedValue(value, symbols);
        if (unsupported == null)
        {
            return;
        }

        ReportUnsupportedType(
            context,
            unsupported.Type,
            unsupported.Location,
            use,
            symbols);
    }

    private static UnsupportedValue? FindUnsupportedValue(
        IOperation operation,
        WorkflowSymbols symbols)
    {
        operation = UnwrapConversions(operation);
        if (operation.ConstantValue.HasValue &&
            operation.ConstantValue.Value == null)
        {
            return null;
        }

        if (operation is IDelegateCreationOperation delegateCreation)
        {
            return FindUnsupportedDelegateResult(delegateCreation.Target, symbols);
        }

        if (operation is IAnonymousFunctionOperation anonymousFunction)
        {
            return FindUnsupportedAnonymousFunctionResult(anonymousFunction, symbols);
        }

        if (operation is IConditionalOperation conditional)
        {
            UnsupportedValue? whenTrue = conditional.WhenTrue == null
                ? null
                : FindUnsupportedValue(conditional.WhenTrue, symbols);
            return whenTrue ?? (conditional.WhenFalse == null
                ? null
                : FindUnsupportedValue(conditional.WhenFalse, symbols));
        }

        if (operation is ICoalesceOperation coalesce)
        {
            UnsupportedValue? value = FindUnsupportedValue(coalesce.Value, symbols);
            return value ?? (coalesce.WhenNull == null
                ? null
                : FindUnsupportedValue(coalesce.WhenNull, symbols));
        }

        if (operation.Type != null &&
            symbols.IsKnownUnsupportedScalar(operation.Type))
        {
            return new UnsupportedValue(operation.Type, operation.Syntax.GetLocation());
        }

        return null;
    }

    private static UnsupportedValue? FindUnsupportedDelegateResult(
        IOperation target,
        WorkflowSymbols symbols)
    {
        target = UnwrapConversions(target);
        if (target is IDelegateCreationOperation delegateCreation)
        {
            return FindUnsupportedDelegateResult(delegateCreation.Target, symbols);
        }

        if (target is IAnonymousFunctionOperation anonymousFunction)
        {
            return FindUnsupportedAnonymousFunctionResult(anonymousFunction, symbols);
        }

        if (target is IMethodReferenceOperation methodReference &&
            symbols.IsKnownUnsupportedScalar(methodReference.Method.ReturnType))
        {
            return new UnsupportedValue(
                methodReference.Method.ReturnType,
                methodReference.Syntax.GetLocation());
        }

        if (target.Type is INamedTypeSymbol delegateType &&
            delegateType.TypeKind == Microsoft.CodeAnalysis.TypeKind.Delegate &&
            delegateType.DelegateInvokeMethod != null &&
            symbols.IsKnownUnsupportedScalar(
                delegateType.DelegateInvokeMethod.ReturnType))
        {
            return new UnsupportedValue(
                delegateType.DelegateInvokeMethod.ReturnType,
                target.Syntax.GetLocation());
        }

        return null;
    }

    private static UnsupportedValue? FindUnsupportedAnonymousFunctionResult(
        IAnonymousFunctionOperation anonymousFunction,
        WorkflowSymbols symbols)
    {
        var pending = new Stack<IOperation>();
        pending.Push(anonymousFunction.Body);

        while (pending.Count != 0)
        {
            IOperation current = pending.Pop();
            if (current != anonymousFunction.Body &&
                (current is IAnonymousFunctionOperation ||
                    current is ILocalFunctionOperation))
            {
                continue;
            }

            if (current is IReturnOperation returnOperation &&
                returnOperation.ReturnedValue != null)
            {
                UnsupportedValue? unsupported =
                    FindUnsupportedValue(returnOperation.ReturnedValue, symbols);
                if (unsupported != null)
                {
                    return unsupported;
                }
            }

            foreach (IOperation child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static void ReportUnsupportedType(
        OperationAnalysisContext context,
        ITypeSymbol type,
        Location location,
        string use,
        WorkflowSymbols symbols)
    {
        if (!symbols.IsKnownUnsupportedScalar(type))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ProcessWorkflowDiagnostics.UnsupportedScalarType,
            location,
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            use));
    }

    private sealed class UnsupportedValue
    {
        internal UnsupportedValue(ITypeSymbol type, Location location)
        {
            Type = type;
            Location = location;
        }

        internal ITypeSymbol Type { get; }
        internal Location Location { get; }
    }

    private sealed class WorkflowSymbols
    {
        private WorkflowSymbols(
            INamedTypeSymbol modelTask,
            INamedTypeSymbol modelTaskOfT,
            INamedTypeSymbol modelAwaitableOfT,
            INamedTypeSymbol modelContextOfT,
            INamedTypeSymbol processSystemModelOfT,
            INamedTypeSymbol processFailureDomainOfT,
            INamedTypeSymbol modelUnit,
            INamedTypeSymbol dateTime,
            INamedTypeSymbol dateTimeOffset,
            INamedTypeSymbol timeSpan,
            INamedTypeSymbol guid,
            INamedTypeSymbol valueType,
            INamedTypeSymbol enumType)
        {
            ModelTask = modelTask;
            ModelTaskOfT = modelTaskOfT;
            ModelAwaitableOfT = modelAwaitableOfT;
            ModelContextOfT = modelContextOfT;
            ProcessSystemModelOfT = processSystemModelOfT;
            ProcessFailureDomainOfT = processFailureDomainOfT;
            ModelUnit = modelUnit;
            DateTime = dateTime;
            DateTimeOffset = dateTimeOffset;
            TimeSpan = timeSpan;
            Guid = guid;
            ValueType = valueType;
            EnumType = enumType;
        }

        private INamedTypeSymbol ModelTask { get; }
        private INamedTypeSymbol ModelTaskOfT { get; }
        private INamedTypeSymbol ModelAwaitableOfT { get; }
        private INamedTypeSymbol ModelContextOfT { get; }
        private INamedTypeSymbol ProcessSystemModelOfT { get; }
        private INamedTypeSymbol ProcessFailureDomainOfT { get; }
        private INamedTypeSymbol ModelUnit { get; }
        private INamedTypeSymbol DateTime { get; }
        private INamedTypeSymbol DateTimeOffset { get; }
        private INamedTypeSymbol TimeSpan { get; }
        private INamedTypeSymbol Guid { get; }
        private INamedTypeSymbol ValueType { get; }
        private INamedTypeSymbol EnumType { get; }

        internal static WorkflowSymbols? Create(Compilation compilation)
        {
            INamedTypeSymbol? modelTask = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ModelTask");
            INamedTypeSymbol? modelTaskOfT = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ModelTask`1");
            INamedTypeSymbol? modelAwaitableOfT = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ModelAwaitable`1");
            INamedTypeSymbol? modelContextOfT = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ModelContext`1");
            INamedTypeSymbol? processSystemModelOfT = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ProcessSystemModel`1");
            INamedTypeSymbol? processFailureDomainOfT = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ProcessFailureDomain`1");
            INamedTypeSymbol? modelUnit = compilation.GetTypeByMetadataName(
                "Microsoft.Accordant.ModelChecking.Experimental.Coroutines.ModelUnit");
            INamedTypeSymbol? dateTime = compilation.GetTypeByMetadataName("System.DateTime");
            INamedTypeSymbol? dateTimeOffset =
                compilation.GetTypeByMetadataName("System.DateTimeOffset");
            INamedTypeSymbol? timeSpan = compilation.GetTypeByMetadataName("System.TimeSpan");
            INamedTypeSymbol? guid = compilation.GetTypeByMetadataName("System.Guid");
            INamedTypeSymbol? valueType = compilation.GetTypeByMetadataName("System.ValueType");
            INamedTypeSymbol? enumType = compilation.GetTypeByMetadataName("System.Enum");

            if (modelTask == null ||
                modelTaskOfT == null ||
                modelAwaitableOfT == null ||
                modelContextOfT == null ||
                processSystemModelOfT == null ||
                processFailureDomainOfT == null ||
                modelUnit == null ||
                dateTime == null ||
                dateTimeOffset == null ||
                timeSpan == null ||
                guid == null ||
                valueType == null ||
                enumType == null)
            {
                return null;
            }

            return new WorkflowSymbols(
                modelTask,
                modelTaskOfT,
                modelAwaitableOfT,
                modelContextOfT,
                processSystemModelOfT,
                processFailureDomainOfT,
                modelUnit,
                dateTime,
                dateTimeOffset,
                timeSpan,
                guid,
                valueType,
                enumType);
        }

        internal bool IsModelTask(ITypeSymbol? type)
            => SymbolEqualityComparer.Default.Equals(type, ModelTask) ||
                IsConstructedFrom(type, ModelTaskOfT);

        internal bool IsModelAwaitable(ITypeSymbol? type)
            => IsConstructedFrom(type, ModelAwaitableOfT);

        internal bool IsModelContextMethod(IMethodSymbol method)
            => IsConstructedFrom(method.ContainingType, ModelContextOfT);

        internal bool IsProcessRegistrationMethod(IMethodSymbol method)
            => method.Name == "RepeatedAction" &&
                (IsConstructedFrom(method.ContainingType, ProcessSystemModelOfT) ||
                    IsConstructedFrom(method.ContainingType, ProcessFailureDomainOfT));

        internal bool IsModelContextCheckpoint(IMethodSymbol method)
        {
            if (!IsModelContextMethod(method))
            {
                return false;
            }

            return IsModelAwaitable(method.ReturnType) ||
                IsModelTask(method.ReturnType);
        }

        internal bool HasScalarCheckpointResult(IMethodSymbol method)
        {
            if (!IsModelContextMethod(method))
            {
                return false;
            }

            return method.Name == "Read" ||
                method.Name == "Choose" ||
                method.Name == "ChooseStep" ||
                method.Name == "WaitUntil" ||
                method.Name == "Call";
        }

        internal bool IsKnownUnsupportedScalar(ITypeSymbol type)
        {
            if (type is IErrorTypeSymbol ||
                type is IDynamicTypeSymbol ||
                type is ITypeParameterSymbol ||
                type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface)
            {
                return false;
            }

            if (type is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return IsKnownUnsupportedScalar(namedType.TypeArguments[0]);
            }

            if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum ||
                IsAllowedSpecialType(type.SpecialType) ||
                SymbolEqualityComparer.Default.Equals(type, ModelUnit) ||
                SymbolEqualityComparer.Default.Equals(type, DateTime) ||
                SymbolEqualityComparer.Default.Equals(type, DateTimeOffset) ||
                SymbolEqualityComparer.Default.Equals(type, TimeSpan) ||
                SymbolEqualityComparer.Default.Equals(type, Guid))
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Object ||
                SymbolEqualityComparer.Default.Equals(type, ValueType) ||
                SymbolEqualityComparer.Default.Equals(type, EnumType))
            {
                return false;
            }

            return true;
        }

        private static bool IsConstructedFrom(
            ITypeSymbol? type,
            INamedTypeSymbol definition)
            => type is INamedTypeSymbol namedType &&
                SymbolEqualityComparer.Default.Equals(
                    namedType.OriginalDefinition,
                    definition);

        private static bool IsAllowedSpecialType(SpecialType type)
        {
            switch (type)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                    return true;

                default:
                    return false;
            }
        }
    }
}
