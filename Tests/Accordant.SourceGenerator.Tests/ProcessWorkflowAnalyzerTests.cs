// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.SourceGenerator.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

[TestFixture]
public sealed class ProcessWorkflowAnalyzerTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Microsoft.Accordant;
        using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

        public enum TestAction
        {
            Run
        }

        public sealed class TestState : State
        {
            public int Value { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new TestState { Value = Value };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths,
                string path,
                bool forceRecompute)
                => Value.ToString();

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        public sealed class Payload
        {
        }

        public readonly struct CustomAwaitable
        {
            public CustomAwaiter GetAwaiter() => new CustomAwaiter();
        }

        public readonly struct CustomAwaiter : INotifyCompletion
        {
            public bool IsCompleted => true;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation) => continuation();
        }

        """;

    [Test]
    public async Task DirectModelTaskAwaitsAreErrors()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public static class Workflows
            {
                private static async ModelTask Helper(ModelContext<TestState> context)
                {
                    await context.Step("helper", state => state.Value++);
                }

                private static async ModelTask<int> ValueHelper(ModelContext<TestState> context)
                {
                    await context.Step("value-helper", state => state.Value++);
                    return 1;
                }

                public static async ModelTask BlockBodied(ModelContext<TestState> context)
                {
                    await Helper(context);
                }

                public static async ModelTask<int> ExpressionBodied(
                    ModelContext<TestState> context)
                    => await ValueHelper(context);
            }
            """);

        AssertIds(diagnostics, "ACC1001", "ACC1001");
        Assert.That(
            diagnostics.All(diagnostic =>
                diagnostic.GetMessage().Contains("ModelContext<TState>.Call")),
            Is.True);
    }

    [Test]
    public async Task ForeignAwaitsAreErrorsAndModelAwaitablesAreAccepted()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public static class Workflows
            {
                public static async ModelTask BlockBodied(ModelContext<TestState> context)
                {
                    await Task.CompletedTask;
                    await ValueTask.CompletedTask;
                    await new CustomAwaitable();
                    await context.Step("valid", state => state.Value++);
                }

                public static async ModelTask ExpressionBodied(
                    ModelContext<TestState> context)
                    => await Task.Yield();

                public static ProcessSystemModel<TestState> Build()
                    => new ProcessSystemModel<TestState>(new TestState())
                        .Process(
                            "expression-lambda",
                            async context => await Task.CompletedTask)
                        .Process(
                            "block-lambda",
                            async context =>
                            {
                                await Task.CompletedTask;
                            });
            }
            """);

        AssertIds(
            diagnostics,
            "ACC1002",
            "ACC1002",
            "ACC1002",
            "ACC1002",
            "ACC1002",
            "ACC1002");
    }

    [Test]
    public async Task RawCheckpointLoopsAreErrorsButSynchronousLoopsAreAccepted()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public static class Workflows
            {
                public static async ModelTask Invalid(ModelContext<TestState> context)
                {
                    while (context != null)
                    {
                        await context.Step("while", state => state.Value++);
                        break;
                    }

                    for (var index = 0; index < 1; index++)
                    {
                        await context.Read("for", state => state.Value);
                    }

                    foreach (var value in new[] { 1 })
                    {
                        await context.Choose("foreach", new[] { value });
                    }

                    do
                    {
                        await context.When("do", _ => true);
                    }
                    while (false);
                }

                public static async ModelTask Valid(ModelContext<TestState> context)
                {
                    var total = 0;
                    for (var index = 0; index < 2; index++)
                    {
                        total += index;
                    }

                    foreach (var value in new[] { 1, 2 })
                    {
                        total += value;
                    }

                    while (total < 4)
                    {
                        total++;
                    }

                    do
                    {
                        total--;
                    }
                    while (total > 3);

                    await context.Step("valid", state => state.Value = total);
                }
            }
            """);

        AssertIds(diagnostics, "ACC1003", "ACC1003", "ACC1003", "ACC1003");
        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.GetMessage()),
            Is.EquivalentTo(new[]
            {
                RawLoopMessage("while"),
                RawLoopMessage("for"),
                RawLoopMessage("foreach"),
                RawLoopMessage("do")
            }));
    }

    [Test]
    public async Task KnownUnsupportedProcessValueTypesAreErrors()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public static class Workflows
            {
                private static async ModelTask ArgumentHelper(
                    ModelContext<TestState> context,
                    Payload payload)
                {
                    await context.Step("argument-helper", state => state.Value++);
                }

                private static async ModelTask<Payload> ResultHelper(
                    ModelContext<TestState> context)
                {
                    await context.Step("result-helper", state => state.Value++);
                    return new Payload();
                }

                private static async ModelTask Iteration(
                    ModelContext<TestState> context,
                    Payload payload)
                {
                    await context.Step("iteration", state => state.Value++);
                }

                private static Payload SubjectOf(int value) => new Payload();

                public static ModelTask Root(ModelContext<TestState> context)
                    => context.Forever("forever", new Payload(), Iteration);

                public static async ModelTask Invalid(ModelContext<TestState> context)
                {
                    _ = await context.Read("read", _ => new Payload());
                    _ = await context.Choose("choose", new[] { new Payload() });
                    _ = await context.ChooseStep(
                        "unsupported-choice-step",
                        TestAction.Run,
                        new[] { new Payload() },
                        (_, _) => { });
                    _ = await context.WaitUntil(
                        "wait",
                        _ => true,
                        _ => new Payload());
                    await context.Call("argument", new Payload(), ArgumentHelper);
                    await context.Call("result", ResultHelper);
                    await context.Step(
                        TestAction.Run,
                        _ => { },
                        subject: new Payload());
                    await context.ChooseStep(
                        "choose-step",
                        TestAction.Run,
                        new[] { 1 },
                        (_, _) => { },
                        subject: _ => new Payload());
                    await context.ChooseStep(
                        "method-group-subject",
                        TestAction.Run,
                        new[] { 1 },
                        (_, _) => { },
                        subject: SubjectOf);
                }

                public static ProcessSystemModel<TestState> Build()
                    => new ProcessSystemModel<TestState>(new TestState())
                        .RepeatedAction(
                            "pulse",
                            TestAction.Run,
                            _ => { },
                            subject: new Payload())
                        .Process("root", Root);
            }
            """);

        AssertIds(
            diagnostics,
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004",
            "ACC1004");

        string[] messages = diagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Some.Contains("Read result"));
            Assert.That(messages, Has.Some.Contains("Choose result"));
            Assert.That(messages, Has.Some.Contains("ChooseStep result"));
            Assert.That(messages, Has.Some.Contains("WaitUntil result"));
            Assert.That(messages, Has.Some.Contains("Call argument"));
            Assert.That(messages, Has.Some.Contains("Call result"));
            Assert.That(messages, Has.Some.Contains("Forever argument"));
            Assert.That(messages, Has.Some.Contains("Step subject"));
            Assert.That(messages, Has.Some.Contains("ChooseStep subject"));
            Assert.That(messages, Has.Some.Contains("RepeatedAction subject"));
        });
    }

    [Test]
    public async Task StructuredWorkflowsAndScalarValuesAreAccepted()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public enum Choice
            {
                First,
                Second
            }

            public static class Workflows
            {
                private static string SubjectName(Choice selected)
                    => selected.ToString();

                public static ProcessSystemModel<TestState> Build()
                    => new ProcessSystemModel<TestState>(new TestState())
                        .RepeatedAction(
                            "pulse",
                            TestAction.Run,
                            _ => { },
                            subject: Guid.Empty)
                        .Process(
                            "worker",
                            context => context.Forever(
                                "worker-loop",
                                1,
                                Iteration));

                private static async ModelTask Iteration(
                    ModelContext<TestState> context,
                    int iteration)
                {
                    var total = iteration;
                    for (var index = 0; index < 2; index++)
                    {
                        total += index;
                    }

                    foreach (var value in new[] { 1, 2 })
                    {
                        total += value;
                    }

                    while (total < 5)
                    {
                        total++;
                    }

                    do
                    {
                        total--;
                    }
                    while (total > 4);

                    _ = await context.Read("read", state => state.Value);
                    _ = await context.Choose(
                        "choose",
                        new[] { Choice.First, Choice.Second });
                    Func<Choice, string> subject = SubjectName;
                    _ = await context.ChooseStep(
                        "choose-step",
                        TestAction.Run,
                        new[] { Choice.First, Choice.Second },
                        (state, selected) => state.Value = (int)selected,
                        subject: subject);
                    _ = await context.WaitUntil(
                        "wait",
                        _ => true,
                        _ => new DateTime(0));
                    DateTimeOffset? result = await context.Call(
                        "helper",
                        TimeSpan.Zero,
                        Helper);
                    await context.Step(
                        TestAction.Run,
                        state => state.Value = total,
                        subject: result);
                }

                private static async ModelTask<DateTimeOffset?> Helper(
                    ModelContext<TestState> context,
                    TimeSpan delay)
                {
                    await context.When("ready", _ => delay == TimeSpan.Zero);
                    return null;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GenericAndObjectProcessValueTypesRemainRuntimeChecked()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public static class Workflows
            {
                private static async ModelTask<T> GenericHelper<T>(
                    ModelContext<TestState> context,
                    T value)
                {
                    await context.Step("helper", _ => { });
                    return value;
                }

                private static async ModelTask GenericIteration<T>(
                    ModelContext<TestState> context,
                    T value)
                {
                    await context.Step(
                        TestAction.Run,
                        _ => { },
                        subject: value);
                }

                public static ModelTask GenericRoot<T>(
                    ModelContext<TestState> context,
                    T value)
                    => context.Forever("generic-loop", value, GenericIteration<T>);

                public static async ModelTask Generic<T>(
                    ModelContext<TestState> context,
                    T value,
                    object unknown)
                {
                    _ = await context.Read("read", _ => value);
                    _ = await context.Choose("choose", new[] { value });
                    _ = await context.WaitUntil("wait", _ => true, _ => value);
                    _ = await context.Call("call", value, GenericHelper<T>);
                    await context.ChooseStep(
                        "choose-step",
                        TestAction.Run,
                        new[] { value },
                        (_, _) => { },
                        subject: selected => selected);
                    await context.Step(
                        TestAction.Run,
                        _ => { },
                        subject: unknown);
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task LookalikeMethodNamesOutsideTheAccordantTypesAreIgnored()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Prelude + """
            public sealed class LookalikeContext
            {
                public Payload Read() => new Payload();

                public Payload Choose() => new Payload();

                public void Call(Payload argument)
                {
                }

                public void Forever(Payload argument)
                {
                }

                public void Step(object subject)
                {
                }
            }

            public sealed class LookalikeModel
            {
                public void RepeatedAction(object subject)
                {
                }
            }

            public static class Workflows
            {
                public static void UseLookalikes(
                    LookalikeContext context,
                    LookalikeModel model)
                {
                    _ = context.Read();
                    _ = context.Choose();
                    context.Call(new Payload());
                    context.Forever(new Payload());
                    context.Step(new Payload());
                    model.RepeatedAction(new Payload());
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    private static string RawLoopMessage(string loopKind)
        => $"Raw '{loopKind}' loop contains an Accordant model checkpoint; " +
            "use ModelContext<TState>.Forever or an appropriate structured process construct";

    private static void AssertIds(
        ImmutableArray<Diagnostic> diagnostics,
        params string[] expected)
        => Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EquivalentTo(expected),
            string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic => diagnostic.GetMessage())));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProcessWorkflowAnalyzerTest",
            new[] { syntaxTree },
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Diagnostic[] compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            compilerErrors,
            Is.Empty,
            string.Join(Environment.NewLine, compilerErrors.Select(error => error.ToString())));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                new ProcessWorkflowAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> References()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? platformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (platformAssemblies != null)
        {
            foreach (string path in platformAssemblies.Split(Path.PathSeparator))
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(State).Assembly.Location);
        paths.Add(typeof(StateGraphNode).Assembly.Location);
        paths.Add(typeof(ModelTask).Assembly.Location);

        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }
}
