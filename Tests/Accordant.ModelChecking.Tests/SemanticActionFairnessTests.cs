namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Ltl;
using NUnit.Framework;

[TestFixture]
public class SemanticActionFairnessTests
{
    private enum SemanticAction
    {
        Administrative,
        Work,
        Publish,
        Accept,
        Escape,
        Progress
    }

    private sealed class ProcessLikeTransition
    {
        public ProcessLikeTransition(
            SemanticAction semanticAction,
            string subject = null,
            string processRole = "scheduler",
            bool isControl = false)
        {
            SemanticAction = semanticAction;
            Subject = subject;
            ProcessRole = processRole;
            IsControl = isControl;
        }

        public SemanticAction SemanticAction { get; }
        public string Subject { get; }
        public string ProcessRole { get; }
        public bool IsControl { get; }
    }

    private sealed class TestState : State
    {
        public int Value { get; }

        public TestState(int value)
        {
            Value = value;
        }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new TestState(Value);

        protected override void LockComponents(HashSet<object> visited)
        {
        }

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Value={Value}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class NamedStep : IStepFunction
    {
        public NamedStep(string id)
        {
            StepFunctionId = id;
        }

        public string StepFunctionId { get; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => null;
    }

    private static StateGraphNode Node(int value, params IStepFunction[] steps)
    {
        var state = new TestState(value);
        state.Freeze();
        return new StateGraphNode
        {
            State = state,
            StepFunctions = new List<IStepFunction>(steps),
            Edges = new List<StateGraphEdge>()
        };
    }

    private static void AddEdge(
        StateGraphNode source,
        StateGraphNode target,
        IStepFunction step,
        ProcessLikeTransition metadata)
        => source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = step,
            Metadata = metadata
        });

    private static StronglyConnectedComponent Scc(
        params StateGraphNode[] nodes)
    {
        var scc = new StronglyConnectedComponent();
        foreach (var node in nodes)
            scc.Nodes.Add(node);
        return scc;
    }

    [Test]
    public void ActionObservationApisAreExplicitlyStutterSensitive()
    {
        Assert.That(
            typeof(FormulaBuilder<TestState>).GetMethod("ObserveAction"),
            Is.Null);
        Assert.That(
            typeof(FormulaBuilder<TestState>).GetMethod("EnabledAction"),
            Is.Null);

        var sensitiveMethods =
            typeof(StutterSensitiveFormulaBuilder<TestState>).GetMethods();
        Assert.That(
            Array.FindAll(
                sensitiveMethods,
                method => method.Name == "ObserveAction"),
            Is.Not.Empty);
        Assert.That(
            Array.FindAll(
                sensitiveMethods,
                method => method.Name == "ObserveAction"),
            Has.All.Property("ReturnType").EqualTo(typeof(TemporalFormula)));
    }

    [Test]
    public void CollectiveAndPerSubjectFairnessHaveDifferentObligations()
    {
        var scheduler = new NamedStep("process-system");
        var first = Node(0, scheduler);
        var second = Node(1, scheduler);
        var outside = Node(2);

        AddEdge(
            first,
            second,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Work, "client-a"));
        AddEdge(
            second,
            first,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Work, "client-a"));
        AddEdge(
            first,
            outside,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Work, "client-b"));
        AddEdge(
            second,
            outside,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Work, "client-b"));

        Func<ProcessLikeTransition, bool> work =
            transition => transition.SemanticAction == SemanticAction.Work;

        Assert.That(
            Fairness.WeakAction(work).IsFairCycle(Scc(first, second)),
            Is.True,
            "the collective family is taken by client-a");
        Assert.That(
            Fairness.WeakEach(
                work,
                transition => transition.Subject)
                .IsFairCycle(Scc(first, second)),
            Is.False,
            "client-b is a separate continuously-enabled obligation");
        Assert.That(
            Fairness.StrongEach(
                work,
                transition => transition.Subject)
                .IsFairCycle(Scc(first, second)),
            Is.False);
    }

    [Test]
    public void SelectedStateNeutralChoiceCanConstrainProductCycles()
    {
        var waitingStep = new NamedStep("process-system#waiting");
        var progressStep = new NamedStep("process-system#accepted");
        var waiting = Node(0, waitingStep);
        var acceptedConfiguration = Node(0, progressStep);
        var goal = Node(1);

        AddEdge(
            waiting,
            waiting,
            waitingStep,
            new ProcessLikeTransition(
                SemanticAction.Administrative,
                isControl: true));
        AddEdge(
            waiting,
            acceptedConfiguration,
            waitingStep,
            new ProcessLikeTransition(SemanticAction.Accept, "request-1"));
        AddEdge(
            acceptedConfiguration,
            goal,
            progressStep,
            new ProcessLikeTransition(SemanticAction.Progress, "request-1"));

        Func<ProcessLikeTransition, bool> accept =
            transition => transition.SemanticAction == SemanticAction.Accept;
        var fairness = Fairness.WeakAction(accept);
        var formula = Formula.For<TestState>();
        var exact = formula.AllowStutterSensitiveFormulas();
        var atGoal = formula.Observe(state => state.Value == 1, "Goal");
        var accepted = exact.ObserveAction(accept, "Accept");
        var canAccept = exact.EnabledAction(accept, "CanAccept");

        Assert.That(
            waiting.State.StringRepresentation(),
            Is.EqualTo(acceptedConfiguration.State.StringRepresentation()));
        Assert.That(waiting.Check(canAccept).Valid, Is.True);
        Assert.That(acceptedConfiguration.Check(canAccept).Valid, Is.False);
        Assert.That(
            waiting.Check(exact.Enabled(_ => true)).Valid,
            Is.False,
            "legacy ENABLED still ignores all neutral edges at the choice");

        Assert.That(waiting.Check(formula.Eventually(atGoal)).Valid, Is.False);
        Assert.That(
            waiting.Check(formula.Eventually(atGoal), fairness: fairness).Valid,
            Is.True);
        Assert.That(waiting.Check(exact.Eventually(accepted)).Valid, Is.False);
        Assert.That(
            waiting.Check(exact.Eventually(accepted), fairness: fairness).Valid,
            Is.True,
            "raw action observations see the selected neutral physical edge");

        var explicitGoal = LtlFormula.Eventually(
            LtlFormula.Prop(
                state => ((TestState)state).Value == 1,
                "Goal"));
        Assert.That(LtlCheck.Check(waiting, explicitGoal).Valid, Is.False);
        Assert.That(
            LtlCheck.Check(waiting, explicitGoal, fairness).Valid,
            Is.True);
    }

    [Test]
    public void UnselectedNeutralAdministrationAndTerminalStutterAreIgnored()
    {
        var scheduler = new NamedStep("process-system#admin");
        var administrative = Node(0, scheduler);
        AddEdge(
            administrative,
            administrative,
            scheduler,
            new ProcessLikeTransition(
                SemanticAction.Administrative,
                isControl: true));

        Func<ProcessLikeTransition, bool> accept =
            transition => transition.SemanticAction == SemanticAction.Accept;
        var fairness = Fairness.StrongAction(accept);

        Assert.That(
            fairness.IsFairCycle(Scc(administrative)),
            Is.True,
            "an unselected neutral control edge creates no obligation");

        var terminal = Node(0);
        var exact = Formula.For<TestState>().AllowStutterSensitiveFormulas();
        var accepted = exact.ObserveAction(accept, "Accept");
        var syntheticStutter = exact.ObserveAction(
            transition => transition.IsStutter,
            "SyntheticStutter");

        Assert.That(terminal.Check(syntheticStutter).Valid, Is.True);
        Assert.That(
            terminal.Check(exact.Eventually(accepted), fairness: fairness).Valid,
            Is.False,
            "the synthetic terminal stutter must remain a fair counterexample");
    }

    [Test]
    public void MetadataIdentityDoesNotCollapseToCompositeStepFunctionId()
    {
        var scheduler = new NamedStep("process-system");
        var first = Node(0, scheduler);
        var second = Node(1, scheduler);
        var outside = Node(2);

        AddEdge(
            first,
            second,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Administrative));
        AddEdge(
            second,
            first,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Administrative));
        AddEdge(
            first,
            outside,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Publish));
        AddEdge(
            second,
            outside,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Publish));

        var cycle = Scc(first, second);

        Assert.That(
            Fairness.Weak(step => step.StepFunctionId == "process-system")
                .IsFairCycle(cycle),
            Is.True,
            "legacy step fairness sees the composite scheduler as taken");
        Assert.That(
            Fairness.WeakAction<ProcessLikeTransition>(
                transition =>
                    transition.SemanticAction == SemanticAction.Publish)
                .IsFairCycle(cycle),
            Is.False,
            "semantic metadata identifies publish independently");
    }

    [Test]
    public void WeakAndStrongUseExactConfigurationsEvenForEqualDomainStates()
    {
        var firstStep = new NamedStep("process-system#first");
        var secondStep = new NamedStep("process-system#second");
        var outsideStep = new NamedStep("process-system#outside");
        var first = Node(0, firstStep);
        var second = Node(0, secondStep);
        var outside = Node(0, outsideStep);

        AddEdge(
            first,
            second,
            firstStep,
            new ProcessLikeTransition(SemanticAction.Administrative));
        AddEdge(
            second,
            first,
            secondStep,
            new ProcessLikeTransition(SemanticAction.Administrative));
        AddEdge(
            first,
            outside,
            firstStep,
            new ProcessLikeTransition(SemanticAction.Escape, "client-a"));

        Func<ProcessLikeTransition, bool> escape =
            transition => transition.SemanticAction == SemanticAction.Escape;
        var cycle = Scc(first, second);
        var exact = Formula.For<TestState>().AllowStutterSensitiveFormulas();
        var enabledEscape = exact.EnabledAction(escape, "EnabledEscape");

        Assert.That(
            first.State.StringRepresentation(),
            Is.EqualTo(second.State.StringRepresentation()));
        Assert.That(first.Check(enabledEscape).Valid, Is.True);
        Assert.That(second.Check(enabledEscape).Valid, Is.False);
        Assert.That(
            Fairness.WeakAction(escape).IsFairCycle(cycle),
            Is.True,
            "escape is disabled at the second exact configuration");
        Assert.That(
            Fairness.StrongAction(escape).IsFairCycle(cycle),
            Is.False,
            "escape is enabled infinitely often at the first configuration");
        Assert.That(
            Fairness.WeakEach(escape, transition => transition.Subject)
                .IsFairCycle(cycle),
            Is.True);
        Assert.That(
            Fairness.StrongEach(escape, transition => transition.Subject)
                .IsFairCycle(cycle),
            Is.False);
    }

    [Test]
    public void GenericTransitionPathCanInspectMetadataActionAndStates()
    {
        var scheduler = new NamedStep("process-system");
        var source = Node(0, scheduler);
        var outside = Node(0);
        AddEdge(
            source,
            source,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Administrative));
        AddEdge(
            source,
            outside,
            scheduler,
            new ProcessLikeTransition(SemanticAction.Accept));

        Func<TestState, Transition, TestState, bool> selected =
            (from, transition, to) =>
                from.Value == to.Value &&
                transition.ActionId == "process-system" &&
                transition.Metadata is ProcessLikeTransition metadata &&
                metadata.SemanticAction == SemanticAction.Accept;

        Assert.That(
            Fairness.WeakAction(selected).IsFairCycle(Scc(source)),
            Is.False);
        Assert.That(
            Fairness.WeakAction<TestState, ProcessLikeTransition>(
                (from, metadata, to) =>
                    from.Value == to.Value &&
                    metadata.SemanticAction == SemanticAction.Accept)
                .IsFairCycle(Scc(source)),
            Is.False);

        var exact = Formula.For<TestState>().AllowStutterSensitiveFormulas();
        Assert.That(source.Check(exact.EnabledAction(selected)).Valid, Is.True);
        Assert.That(
            source.Check(exact.EnabledAction<ProcessLikeTransition>(
                (from, metadata, to) =>
                    from.Value == to.Value &&
                    metadata.SemanticAction == SemanticAction.Accept)).Valid,
            Is.True);
    }
}
