namespace Accordant.ModelChecking.Tests.Refinement;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

[TestFixture]
public class WitnessRefinementTests
{
    private sealed class ConcreteState : State
    {
        public ConcreteState(string stage, string result = null)
        {
            Stage = stage;
            Result = result;
        }

        public string Stage { get; }
        public string Result { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ConcreteState(Stage, Result);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Concrete({Stage},{Result ?? "-"})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class AbstractState : State
    {
        public AbstractState(string stage, string choice = null)
        {
            Stage = stage;
            Choice = choice;
        }

        public string Stage { get; }
        public string Choice { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new AbstractState(Stage, Choice);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Abstract({Stage},{Choice ?? "-"})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class ColorWitness : State
    {
        public ColorWitness(string color)
        {
            Color = color;
        }

        public string Color { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new ColorWitness(Color);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Color({Color})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class DecisionWitness : State
    {
        public DecisionWitness(string decision)
        {
            Decision = decision;
        }

        public string Decision { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new DecisionWitness(Decision);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Decision({Decision})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class MutableWitness : State
    {
        public string Color { get; set; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new MutableWitness { Color = Color };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"Mutable({Color})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class HistoryState : State
    {
        public HistoryState(string owner)
        {
            Owner = owner;
        }

        public string Owner { get; }

        protected override void CloneInternal(Dictionary<object, object> map)
            => map[this] = new HistoryState(Owner);

        protected override string StringRepresentationInternal(
            Dictionary<object, string> paths,
            string path,
            bool forceRecompute)
            => $"History({Owner})";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class LabelStep : IStepFunction
    {
        public LabelStep(string id)
        {
            StepFunctionId = id;
        }

        public string StepFunctionId { get; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => null;
    }

    private sealed class ConcreteWorkflowStep : IStepFunction
    {
        public string StepFunctionId => "workflow";

        public int ApplyCount { get; private set; }

        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
        {
            ApplyCount++;
            var concrete = (ConcreteState)state;
            switch (concrete.Stage)
            {
                case "Idle":
                    return new[] { Result(new ConcreteState("Working")) };

                case "Working":
                    return new[]
                    {
                        Result(new ConcreteState("Done", "red")),
                        Result(new ConcreteState("Done", "blue"))
                    };

                default:
                    return null;
            }
        }

        private StepResult Result(ConcreteState state)
            => new StepResult
            {
                State = state,
                StepFunctions = new IStepFunction[] { this }
            };
    }

    // ---------------------------------------------------------------
    // A result revealed only when the concrete operation completes.
    // ---------------------------------------------------------------

    [Test]
    public void DelayedOutcomeRefinesWithWitness()
    {
        var result = DelayedOutcomeCheck(
            BuildConcrete(),
            BuildAbstract()).Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DelayedOutcomeRefinesTemporally()
    {
        var result = DelayedOutcomeCheck(
            BuildConcrete(),
            BuildAbstract()).CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void DelayedOutcomeCannotBeMappedFromConcreteStateAlone()
    {
        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                BuildConcrete(),
                BuildAbstract())
            .Map(concrete => concrete.Stage switch
            {
                "Idle" => new AbstractState("Idle"),
                "Working" => new AbstractState("Chosen", "red"),
                _ => new AbstractState("Done", concrete.Result)
            })
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void WrongPredictionCopyDiesInsteadOfFailingRefinement()
    {
        var mapped = new List<string>();
        var result = DelayedOutcomeCheck(
            BuildConcrete(),
            BuildAbstract(),
            observed: mapped).Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            mapped,
            Does.Contain("Working:{r1=Color(red)}").And.Contain(
                "Working:{r1=Color(blue)}"),
            "both predictions must be explored");
        Assert.That(
            mapped.Count(entry => entry.StartsWith("Done")),
            Is.EqualTo(2),
            "each completion is reached by exactly one surviving prediction");
    }

    [Test]
    public void MissingAbstractFutureForOnePredictionIsAMismatch()
    {
        var result = DelayedOutcomeCheck(
            BuildConcrete(),
            BuildAbstract(blueCanFinish: false)).Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
        Assert.That(
            result.Trace[result.Trace.Count - 2]
                .Witnesses.Get<ColorWitness>("r1").Color,
            Is.EqualTo("blue"));
        Assert.That(
            result.Trace[result.Trace.Count - 1].Witnesses.Count,
            Is.EqualTo(0),
            "the resolving transition removes the witness");
        Assert.That(
            result.GetTraceString(),
            Does.Contain("witnesses {r1=Color(blue)}"));
    }

    [Test]
    public void WitnessesAreReportedSeparatelyFromAugmentation()
    {
        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                BuildConcrete(),
                BuildAbstract(blueCanFinish: false))
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (history, transition) =>
                    new HistoryState(transition.Target.Stage))
            .WithWitness(WitnessInitial, WitnessNext)
            .Map((concrete, history, witnesses) => MapDelayedOutcome(
                concrete,
                witnesses))
            .Check();

        var failing = result.Trace[result.Trace.Count - 2];
        Assert.That(
            ((HistoryState)failing.AuxiliaryState).Owner,
            Is.EqualTo("Working"));
        Assert.That(
            failing.Witnesses.Get<ColorWitness>("r1").Color,
            Is.EqualTo("blue"));
        Assert.That(
            result.GetTraceString(),
            Does.Contain("auxiliary History(Working)").And.Contain(
                "witnesses {r1=Color(blue)}"));
    }

    // ---------------------------------------------------------------
    // Several operations pending at once.
    // ---------------------------------------------------------------

    [Test]
    public void SimultaneousOperationsFormTheCrossProductAndPruneIndependently()
    {
        var started = ConcreteNode("Started");
        var bResolved = ConcreteNode("BResolved");
        var aResolved = ConcreteNode("AResolved");
        var root = ConcreteNode("Idle");
        AddEdge(root, started, "start-both");
        AddEdge(started, bResolved, "resolve-b");
        AddEdge(bResolved, aResolved, "resolve-a");

        var observed = new List<string>();
        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                    transition.StepFunction.StepFunctionId switch
                    {
                        "start-both" => WitnessChanges
                            .Introduce(
                                "a",
                                new ColorWitness("red"),
                                new ColorWitness("blue"))
                            .And(WitnessChanges.Introduce(
                                "b",
                                new DecisionWitness("accepted"),
                                new DecisionWitness("rejected"))),
                        "resolve-b" => WitnessChanges.Resolve(
                            "b",
                            new DecisionWitness("rejected")),
                        "resolve-a" => WitnessChanges.Resolve(
                            "a",
                            new ColorWitness("blue")),
                        _ => WitnessChanges.None
                    })
            .Map((concrete, witnesses) =>
            {
                observed.Add($"{concrete.Stage}:{witnesses}");
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            observed.Where(entry => entry.StartsWith("Started")).OrderBy(x => x),
            Is.EqualTo(new[]
            {
                "Started:{a=Color(blue), b=Decision(accepted)}",
                "Started:{a=Color(blue), b=Decision(rejected)}",
                "Started:{a=Color(red), b=Decision(accepted)}",
                "Started:{a=Color(red), b=Decision(rejected)}"
            }));
        Assert.That(
            observed.Where(entry => entry.StartsWith("BResolved")).OrderBy(x => x),
            Is.EqualTo(new[]
            {
                "BResolved:{a=Color(blue)}",
                "BResolved:{a=Color(red)}"
            }),
            "resolving b keeps both predictions for the still pending a");
        Assert.That(
            observed.Where(entry => entry.StartsWith("AResolved")),
            Is.EqualTo(new[] { "AResolved:{}" }));
    }

    // ---------------------------------------------------------------
    // Retention, cancellation, and identity.
    // ---------------------------------------------------------------

    [Test]
    public void CancellationRetainsEveryCopyAndMergesThem()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var cancelled = ConcreteNode("Cancelled");
        AddEdge(root, working, "start");
        AddEdge(working, cancelled, "cancel");

        var observed = new List<string>();
        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                    transition.StepFunction.StepFunctionId switch
                    {
                        "start" => WitnessChanges.Introduce(
                            "r1",
                            new ColorWitness("red"),
                            new ColorWitness("blue")),
                        "cancel" => WitnessChanges.Cancel("r1"),
                        _ => WitnessChanges.None
                    })
            .Map((concrete, witnesses) =>
            {
                observed.Add($"{concrete.Stage}:{witnesses}");
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            observed.Count(entry => entry.StartsWith("Working")),
            Is.EqualTo(2));
        Assert.That(
            observed.Where(entry => entry.StartsWith("Cancelled")),
            Is.EqualTo(new[] { "Cancelled:{}" }),
            "cancelled copies become identical and merge");
    }

    [Test]
    public void ResolutionMayBeFollowedByReintroductionInOneTransition()
    {
        var root = ConcreteNode("Idle");
        var attempt1 = ConcreteNode("Attempt1");
        var attempt2 = ConcreteNode("Attempt2");
        var done = ConcreteNode("Done");
        AddEdge(root, attempt1, "start");
        AddEdge(attempt1, attempt2, "retry");
        AddEdge(attempt2, done, "finish");

        var observed = new List<string>();
        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                    transition.StepFunction.StepFunctionId switch
                    {
                        "start" => Colors("r1"),
                        "retry" => WitnessChanges
                            .Resolve("r1", new ColorWitness("red"))
                            .And(Colors("r1")),
                        "finish" => WitnessChanges.Resolve(
                            "r1",
                            new ColorWitness("blue")),
                        _ => WitnessChanges.None
                    })
            .Map((concrete, witnesses) =>
            {
                observed.Add($"{concrete.Stage}:{witnesses}");
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            observed.Count(entry => entry.StartsWith("Attempt1")),
            Is.EqualTo(2));
        Assert.That(
            observed.Count(entry => entry.StartsWith("Attempt2")),
            Is.EqualTo(2),
            "the reintroduced domain branches again after the resolution");
        Assert.That(
            observed.Where(entry => entry.StartsWith("Done")),
            Is.EqualTo(new[] { "Done:{}" }));
    }

    [Test]
    public void PathsWithDifferentIntroducedDomainsDoNotMerge()
    {
        var root = ConcreteNode("Idle");
        var pending = ConcreteNode("Pending");
        AddEdge(root, pending, "start-a");
        AddEdge(root, pending, "start-b");

        var observed = new List<string>();
        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, transition) =>
                    transition.StepFunction.StepFunctionId == "start-a"
                        ? WitnessChanges.Introduce(
                            "r1",
                            new ColorWitness("red"),
                            new ColorWitness("blue"))
                        : transition.StepFunction.StepFunctionId == "start-b"
                            ? WitnessChanges.Introduce(
                                "r1",
                                new ColorWitness("red"),
                                new ColorWitness("green"))
                            : WitnessChanges.None)
            .Map((concrete, witnesses) =>
            {
                observed.Add($"{concrete.Stage}:{witnesses}");
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            observed.Count(entry => entry.StartsWith("Pending")),
            Is.EqualTo(4),
            "the two red copies carry different domains and must stay apart");
    }

    [Test]
    public void LifecycleCallbackSeesPendingIdentitiesAndDeclaredDomains()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var done = ConcreteNode("Done", "red");
        AddEdge(root, working, "start");
        AddEdge(working, done, "complete");
        var seen = new List<string>();

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: concrete =>
                {
                    Assert.That(concrete.Stage, Is.EqualTo("Idle"));
                    return WitnessChanges.None;
                },
                next: (pending, transition) =>
                {
                    seen.Add(pending.ToString());
                    if (transition.StepFunction.StepFunctionId == "start")
                    {
                        Assert.That(pending.Count, Is.Zero);
                        Assert.That(pending.IsPending("r1"), Is.False);
                        return Colors("r1");
                    }

                    Assert.That(pending.IsPending("r1"), Is.True);
                    Assert.That(
                        pending.GetDomain("r1").Select(value => value.ToString()),
                        Is.EquivalentTo(new[] { "Color(red)", "Color(blue)" }));
                    return WitnessChanges.Resolve(
                        "r1",
                        new ColorWitness(transition.Target.Result));
                })
            .Map((concrete, witnesses) => new AbstractState("Idle"))
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(seen, Does.Contain("{}"));
        Assert.That(
            seen,
            Does.Contain("{r1 in [Color(blue), Color(red)]}").Or.Contain(
                "{r1 in [Color(red), Color(blue)]}"));
    }

    // ---------------------------------------------------------------
    // Reading witnesses from the mapping.
    // ---------------------------------------------------------------

    [Test]
    public void GetReportsAbsentIdentitiesAndWrongValueTypes()
    {
        var witnesses = SingleWitnessCollection();

        Assert.That(
            () => witnesses.Get<ColorWitness>("missing"),
            Throws.InvalidOperationException.With.Message.Contains(
                "No witness is pending for operation 'missing'"));
        Assert.That(
            () => witnesses.Get<DecisionWitness>("r1"),
            Throws.InvalidOperationException.With.Message.Contains(
                "not a"));
        Assert.That(witnesses.Get<ColorWitness>("r1").Color, Is.EqualTo("red"));
    }

    [Test]
    public void TryGetAndIsPendingKeepMappingsTotal()
    {
        var witnesses = SingleWitnessCollection();

        Assert.That(witnesses.IsPending("r1"), Is.True);
        Assert.That(witnesses.IsPending("other"), Is.False);
        Assert.That(witnesses.Count, Is.EqualTo(1));
        Assert.That(witnesses.PendingOperations, Is.EqualTo(new[] { "r1" }));
        Assert.That(
            witnesses.TryGet<ColorWitness>("r1", out var color),
            Is.True);
        Assert.That(color.Color, Is.EqualTo("red"));
        Assert.That(
            witnesses.TryGet<ColorWitness>("other", out _),
            Is.False);
        Assert.That(
            witnesses.TryGet<DecisionWitness>("r1", out _),
            Is.False);
    }

    [Test]
    public void MappingSpanningPendingAndResolvedPositionsStaysTotal()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var done = ConcreteNode("Done", "red");
        AddEdge(root, working, "start");
        AddEdge(working, done, "complete");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, transition) =>
                    transition.StepFunction.StepFunctionId == "start"
                        ? Colors("r1")
                        : WitnessChanges.Resolve(
                            "r1",
                            new ColorWitness(transition.Target.Result)))
            .Map((concrete, witnesses) => witnesses.IsPending("r1")
                ? new AbstractState("Idle")
                : new AbstractState("Idle"))
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Unresolved witnesses on terminal and infinite behaviors.
    // ---------------------------------------------------------------

    [Test]
    public void TerminalStutterRetainsUnresolvedWitnesses()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        var result = TerminalCheck(root, BuildAbstract()).CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void UnresolvedWitnessCannotBeChosenToMakeRefinementPass()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        var result = TerminalCheck(root, BuildAbstract(includeBlue: false))
            .CheckTemporal();

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.DoesNotRefine),
            "every unresolved prediction must satisfy the abstract model");
    }

    // ---------------------------------------------------------------
    // Fairness: concrete enabledness comes from the original graph.
    // ---------------------------------------------------------------

    [Test]
    public void DeadWrongPredictionIsNotTurnedIntoTerminalStutter()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");
        AddEdge(working, ConcreteNode("Done", "red"), "complete");

        var result = DelayedOutcomeCheck(root, BuildAbstract())
            .CheckTemporal(
                concreteFairness: null,
                abstractFairness: Fairness.Weak(
                    step => step.StepFunctionId == "finish"));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "a refuted prediction is a finite dead branch, not an infinite " +
            "stuttering behavior that could violate abstract fairness");
    }

    [Test]
    public void TemporalCheckIgnoresAMismatchOnARefutedFiniteCopy()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");
        AddEdge(working, ConcreteNode("Done", "red"), "complete");

        var safety = DelayedOutcomeCheck(
                root,
                BuildAbstract(includeBlue: false))
            .Check();
        var temporal = DelayedOutcomeCheck(
                root,
                BuildAbstract(includeBlue: false))
            .CheckTemporal();

        Assert.That(
            safety.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch),
            "finite safety checks inspect every witness prefix");
        Assert.That(
            temporal.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "the mismatching blue prefix has no infinite witness extension");
    }

    [Test]
    public void InitialWitnessBranchesThatDoNotAffectTheRootMapping()
    {
        var root = ConcreteNode("Idle");
        AddEdge(root, ConcreteNode("Working"), "start");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => Colors("r1"),
                next: (_, _) => WitnessChanges.None)
            .Map((concrete, witnesses) => new AbstractState("Idle"))
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void InitialWitnessBranchThatChangesTheRootMappingIsRejected()
    {
        var root = ConcreteNode("Idle");
        AddEdge(root, ConcreteNode("Working"), "start");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(
                root,
                AbstractNode("Idle", "red"))
            .WithWitness(
                initial: _ => Colors("r1"),
                next: (_, _) => WitnessChanges.None)
            .Map((concrete, witnesses) => new AbstractState(
                "Idle",
                witnesses.Get<ColorWitness>("r1").Color))
            .Check();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.InitialStateMismatch));
        Assert.That(
            result.Trace[0].Witnesses.Get<ColorWitness>("r1").Color,
            Is.EqualTo("blue"));
    }

    [Test]
    public void DeadInitialMismatchDoesNotMislabelADeeperTemporalMismatch()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "resolve");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => Colors("r1"),
                next: (_, _) => WitnessChanges.Resolve(
                    "r1",
                    new ColorWitness("red")))
            .Map((concrete, witnesses) =>
                concrete.Stage == "Idle"
                    ? new AbstractState(
                        witnesses.Get<ColorWitness>("r1").Color == "red"
                            ? "Idle"
                            : "BadRoot")
                    : new AbstractState("Missing"))
            .CheckTemporal();

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TransitionMismatch));
    }

    [Test]
    public void PollingForeverViolatesAbstractWeakFairness()
    {
        var result = DelayedOutcomeCheck(
                BuildConcrete(withPoll: true),
                BuildAbstract())
            .CheckTemporal(
                concreteFairness: null,
                abstractFairness: Fairness.Weak(
                    step => step.StepFunctionId == "finish"));

        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch));
        Assert.That(
            result.Trace.Any(item => item.IsInCycle),
            Is.True);
    }

    [Test]
    public void WitnessFilteringDoesNotDisableConcreteCompletion()
    {
        var result = DelayedOutcomeCheck(
                BuildConcrete(withPoll: true),
                BuildAbstract())
            .CheckTemporal(
                concreteFairness: Fairness.Weak(
                    step => step.StepFunctionId == "complete"),
                abstractFairness: Fairness.Weak(
                    step => step.StepFunctionId == "finish"));

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            "completion stays continuously enabled in the original concrete " +
            "graph even though one outcome edge kills a prediction copy");
    }

    [Test]
    public void StrongConcreteFairnessAlsoExcludesThePollingCycle()
    {
        var result = DelayedOutcomeCheck(
                BuildConcrete(withPoll: true),
                BuildAbstract())
            .CheckTemporal(
                concreteFairness: Fairness.Strong(
                    step => step.StepFunctionId == "complete"),
                abstractFairness: Fairness.Weak(
                    step => step.StepFunctionId == "finish"));

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Composition with deterministic augmentation.
    // ---------------------------------------------------------------

    [Test]
    public void AugmentationAndWitnessesComposeInOneMapping()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var done = ConcreteNode("Done", "red");
        AddEdge(root, working, "start-alice");
        AddEdge(working, done, "complete");

        var abstraction = AbstractNode("Idle");
        var chosen = AbstractNode("Chosen", "alice/red");
        AddEdge(abstraction, chosen, "choose");
        AddEdge(abstraction, AbstractNode("Chosen", "alice/blue"), "choose");
        AddEdge(chosen, AbstractNode("Done", "alice/red"), "finish");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, abstraction)
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (history, transition) =>
                    transition.StepFunction.StepFunctionId == "start-alice"
                        ? new HistoryState("alice")
                        : new HistoryState(history.Owner))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, transition) =>
                    transition.StepFunction.StepFunctionId == "start-alice"
                        ? Colors("r1")
                        : WitnessChanges.Resolve(
                            "r1",
                            new ColorWitness(transition.Target.Result)))
            .Map((concrete, history, witnesses) => concrete.Stage switch
            {
                "Idle" => new AbstractState("Idle"),
                "Working" => new AbstractState(
                    "Chosen",
                    history.Owner + "/" +
                        witnesses.Get<ColorWitness>("r1").Color),
                _ => new AbstractState(
                    "Done",
                    history.Owner + "/" + concrete.Result)
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void AugmentationAndWitnessesComposeTemporally()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start-alice");

        var abstraction = AbstractNode("Idle");
        AddEdge(abstraction, AbstractNode("Chosen", "alice/red"), "choose");
        AddEdge(abstraction, AbstractNode("Chosen", "alice/blue"), "choose");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, abstraction)
            .Augment(
                initial: _ => new HistoryState("none"),
                next: (_, _) => new HistoryState("alice"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => Colors("r1"))
            .Map((concrete, history, witnesses) => concrete.Stage == "Idle"
                ? new AbstractState("Idle")
                : new AbstractState(
                    "Chosen",
                    history.Owner + "/" +
                        witnesses.Get<ColorWitness>("r1").Color))
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    // ---------------------------------------------------------------
    // Witness-definition errors.
    // ---------------------------------------------------------------

    [Test]
    public void EmptyOrDuplicateDomainsAreRejected()
    {
        Assert.That(
            () => WitnessChanges.Introduce("r1"),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "is empty"));
        Assert.That(
            () => WitnessChanges.Introduce(
                "r1",
                new ColorWitness("red"),
                new ColorWitness("red")),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "duplicate value"));
        Assert.That(
            () => WitnessChanges.Introduce("r1", (IEnumerable<State>)null),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "is null"));
        Assert.That(
            () => WitnessChanges.Introduce("r1", new State[] { null }),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "null value"));
    }

    [Test]
    public void EmptyOperationIdentitiesAreRejected()
    {
        Assert.That(
            () => WitnessChanges.Introduce("", new ColorWitness("red")),
            Throws.TypeOf<WitnessDefinitionException>());
        Assert.That(
            () => WitnessChanges.Cancel(null),
            Throws.TypeOf<WitnessDefinitionException>());
        Assert.That(
            () => WitnessChanges.Resolve("r1", null),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "is null"));
    }

    [Test]
    public void IntroducingAnAlreadyPendingOperationIsRejected()
    {
        Assert.That(
            () => RunLifecycle((_, _) => Colors("r1"), edges: 2),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "already pending"));
    }

    [Test]
    public void IntroducingOneIdentityTwiceInOneTransitionIsRejected()
    {
        Assert.That(
            () => RunLifecycle(
                (_, _) => Colors("r1").And(Colors("r1")),
                edges: 1),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "introduced more than once"));
    }

    [Test]
    public void ResolvingOrCancellingAnUnknownOperationIsRejected()
    {
        Assert.That(
            () => RunLifecycle(
                (_, _) => WitnessChanges.Resolve(
                    "missing",
                    new ColorWitness("red")),
                edges: 1),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "no operation with that identity is pending"));
        Assert.That(
            () => RunLifecycle(
                (_, _) => WitnessChanges.Cancel("missing"),
                edges: 1),
            Throws.TypeOf<WitnessDefinitionException>());
    }

    [Test]
    public void ResolvingWithAValueOutsideTheDomainIsRejected()
    {
        Assert.That(
            () => RunLifecycle(
                (pending, transition) => pending.IsPending("r1")
                    ? WitnessChanges.Resolve("r1", new ColorWitness("green"))
                    : Colors("r1"),
                edges: 2),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "not in the introduced domain"));
    }

    [Test]
    public void ContradictoryLifecycleCombinationsAreRejected()
    {
        Assert.That(
            () => RunLifecycle(
                (_, _) => Colors("r1").And(WitnessChanges.Cancel("r1")),
                edges: 1),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "must precede reintroduction"));
        Assert.That(
            () => RunLifecycle(
                (pending, _) => pending.IsPending("r1")
                    ? WitnessChanges.Cancel("r1").And(
                        WitnessChanges.Cancel("r1"))
                    : Colors("r1"),
                edges: 2),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "more than once"));
    }

    [Test]
    public void NullLifecycleResultsAreRejected()
    {
        Assert.That(
            () => RunLifecycle((_, _) => null, edges: 1),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "update returned null"));

        var root = ConcreteNode("Idle");
        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    root,
                    AbstractNode("Idle"))
                .WithWitness(
                    initial: _ => null,
                    next: (_, _) => WitnessChanges.None)
                .Map((_, _) => new AbstractState("Idle"))
                .Check(),
            Throws.TypeOf<WitnessDefinitionException>().With.Message.Contains(
                "initializer returned null"));
    }

    [Test]
    public void NullWitnessCallbacksAndMappingsAreRejected()
    {
        var builder = Refinement.Between<ConcreteState, AbstractState>(
            ConcreteNode("Idle"),
            AbstractNode("Idle"));

        Assert.That(
            () => builder.WithWitness(null, (_, _) => WitnessChanges.None),
            Throws.ArgumentNullException);
        Assert.That(
            () => builder.WithWitness(_ => WitnessChanges.None, null),
            Throws.ArgumentNullException);
        Assert.That(
            () => builder
                .WithWitness(
                    _ => WitnessChanges.None,
                    (_, _) => WitnessChanges.None)
                .Map(null),
            Throws.ArgumentNullException);

        var augmented = builder.Augment(
            initial: _ => new HistoryState("none"),
            next: (history, _) => history);
        Assert.That(
            () => augmented.WithWitness(null, (_, _) => WitnessChanges.None),
            Throws.ArgumentNullException);
        Assert.That(
            () => augmented.WithWitness(_ => WitnessChanges.None, null),
            Throws.ArgumentNullException);
        Assert.That(
            () => augmented
                .WithWitness(
                    _ => WitnessChanges.None,
                    (_, _) => WitnessChanges.None)
                .Map(null),
            Throws.ArgumentNullException);
    }

    // ---------------------------------------------------------------
    // Freezing and mutation detection.
    // ---------------------------------------------------------------

    [Test]
    public void WitnessValuesAreFrozenBeforeTheyEnterTheMapping()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => Colors("r1"))
            .Map((concrete, witnesses) =>
            {
                if (witnesses.TryGet<ColorWitness>("r1", out var color))
                {
                    Assert.That(color.IsFrozen, Is.True);
                }
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
    }

    [Test]
    public void MutatingAWitnessValueInTheMappingIsRejected()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    root,
                    AbstractNode("Idle"))
                .WithWitness(
                    initial: _ => WitnessChanges.None,
                    next: (_, _) => WitnessChanges.Introduce(
                        "r1",
                        new MutableWitness { Color = "red" },
                        new MutableWitness { Color = "blue" }))
                .Map((concrete, witnesses) =>
                {
                    if (witnesses.TryGet<MutableWitness>("r1", out var value))
                    {
                        value.Color = "changed";
                    }
                    return new AbstractState("Idle");
                })
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void RefreezingCannotHideWitnessMutation()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    root,
                    AbstractNode("Idle"))
                .WithWitness(
                    initial: _ => WitnessChanges.None,
                    next: (_, _) => WitnessChanges.Introduce(
                        "r1",
                        new MutableWitness { Color = "red" },
                        new MutableWitness { Color = "blue" }))
                .Map((concrete, witnesses) =>
                {
                    if (witnesses.TryGet<MutableWitness>("r1", out var value))
                    {
                        value.Color = "changed";
                        value.Freeze();
                    }
                    return new AbstractState("Idle");
                })
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void MutatingAWitnessValueInTheLifecycleIsRejectedBeforeBranchDeath()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var done = ConcreteNode("Done");
        AddEdge(root, working, "start");
        AddEdge(working, done, "resolve");

        Assert.That(
            () => Refinement
                .Between<ConcreteState, AbstractState>(
                    root,
                    AbstractNode("Idle"))
                .WithWitness(
                    initial: _ => WitnessChanges.None,
                    next: (pending, transition) =>
                    {
                        if (transition.StepFunction.StepFunctionId == "start")
                        {
                            return WitnessChanges.Introduce(
                                "r1",
                                new MutableWitness { Color = "red" });
                        }

                        var value =
                            (MutableWitness)pending.GetDomain("r1")[0];
                        value.Color = "blue";
                        value.Freeze();
                        return WitnessChanges.Resolve(
                            "r1",
                            new MutableWitness { Color = "blue" });
                    })
                .Map((concrete, witnesses) => new AbstractState("Idle"))
                .Check(),
            Throws.TypeOf<StateFrozenException>());
    }

    [Test]
    public void PendingWitnessDomainsCannotBeModifiedByLifecycleCallbacks()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        var done = ConcreteNode("Done");
        AddEdge(root, working, "start");
        AddEdge(working, done, "inspect");
        var rejected = false;

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                {
                    if (transition.StepFunction.StepFunctionId == "start")
                    {
                        return Colors("r1");
                    }

                    var domain = (IList<State>)pending.GetDomain("r1");
                    try
                    {
                        domain.Add(new ColorWitness("green"));
                    }
                    catch (NotSupportedException)
                    {
                        rejected = true;
                    }
                    return WitnessChanges.Cancel("r1");
                })
            .Map((concrete, witnesses) => new AbstractState("Idle"))
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(rejected, Is.True);
    }

    [Test]
    public void DelimitersInOperationIdentitiesCannotCollide()
    {
        var root = ConcreteNode("Idle");
        var pending = ConcreteNode("Pending");
        AddEdge(root, pending, "single");
        AddEdge(root, pending, "pair");
        var abstractRoot = AbstractNode("Idle");
        AddEdge(abstractRoot, AbstractNode("Pending", "1"), "one");
        AddEdge(abstractRoot, AbstractNode("Pending", "2"), "two");
        var red = new ColorWitness("red");
        var blue = new ColorWitness("blue");
        red.Freeze();
        blue.Freeze();
        var redIdentity = red.GetStateHash().ToString("X16");
        var maliciousId =
            $"a={redIdentity}@{redIdentity};b";
        var observedCounts = new List<int>();

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, abstractRoot)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, transition) =>
                    transition.StepFunction.StepFunctionId == "single"
                        ? WitnessChanges.Introduce(maliciousId, blue)
                        : WitnessChanges
                            .Introduce("a", red)
                            .And(WitnessChanges.Introduce("b", blue)))
            .Map((concrete, witnesses) =>
            {
                if (concrete.Stage == "Pending")
                {
                    observedCounts.Add(witnesses.Count);
                    return new AbstractState(
                        "Pending",
                        witnesses.Count.ToString());
                }
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(observedCounts, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void SemanticallyEqualWitnessValuesShareOneIdentity()
    {
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");
        AddEdge(working, working, "loop");
        var mappingCalls = 0;

        var result = Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                    transition.StepFunction.StepFunctionId == "start"
                        ? WitnessChanges.Introduce("r1", new ColorWitness("red"))
                        : WitnessChanges.None)
            .Map((concrete, witnesses) =>
            {
                mappingCalls++;
                return new AbstractState("Idle");
            })
            .CheckTemporal();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(
            mappingCalls,
            Is.EqualTo(2),
            "the freshly allocated equal witness value must not re-branch");
    }

    // ---------------------------------------------------------------
    // Exploration modes and bounded graphs.
    // ---------------------------------------------------------------

    [TestCase(false)]
    [TestCase(true)]
    public void WitnessRefinementWorksOverExploredGraphs(bool lazy)
    {
        var step = new ConcreteWorkflowStep();
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { step },
            new ConcreteState("Idle"),
            lazy: lazy);

        var result = ExploredCheck(concrete, BuildAbstract()).Check();

        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.Refines));
        Assert.That(step.ApplyCount, Is.GreaterThan(0));
    }

    [Test]
    public void ConcreteDepthFrontierIsInconclusiveWithWitnesses()
    {
        var concrete = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new ConcreteWorkflowStep() },
            new ConcreteState("Idle"),
            maxDepth: 1,
            lazy: true);

        var result = ExploredCheck(concrete, BuildAbstract()).Check();

        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.InconclusiveBound));
        Assert.That(result.Valid, Is.Null);
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------

    private static WitnessChanges Colors(string operationId)
        => WitnessChanges.Introduce(
            operationId,
            new ColorWitness("red"),
            new ColorWitness("blue"));

    private static WitnessChanges WitnessInitial(ConcreteState concrete)
        => WitnessChanges.None;

    private static WitnessChanges WitnessNext(
        PendingWitnesses pending,
        RefinementTransition<ConcreteState> transition)
    {
        switch (transition.StepFunction.StepFunctionId)
        {
            case "start":
                return Colors("r1");

            case "complete":
                return WitnessChanges.Resolve(
                    "r1",
                    new ColorWitness(transition.Target.Result));

            default:
                return WitnessChanges.None;
        }
    }

    private static AbstractState MapDelayedOutcome(
        ConcreteState concrete,
        WitnessCollection witnesses)
    {
        switch (concrete.Stage)
        {
            case "Idle":
                return new AbstractState("Idle");

            case "Working":
                return new AbstractState(
                    "Chosen",
                    witnesses.Get<ColorWitness>("r1").Color);

            default:
                return new AbstractState("Done", concrete.Result);
        }
    }

    private static WitnessFunctionalRefinementCheck<ConcreteState, AbstractState>
        DelayedOutcomeCheck(
            StateGraphNode concrete,
            StateGraphNode abstraction,
            List<string> observed = null)
        => Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .WithWitness(WitnessInitial, WitnessNext)
            .Map((concreteState, witnesses) =>
            {
                observed?.Add($"{concreteState.Stage}:{witnesses}");
                return MapDelayedOutcome(concreteState, witnesses);
            });

    private static WitnessFunctionalRefinementCheck<ConcreteState, AbstractState>
        ExploredCheck(
            StateGraphNode concrete,
            StateGraphNode abstraction)
        => Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (pending, transition) =>
                    transition.Source.Stage == "Idle"
                        ? Colors("r1")
                        : transition.Source.Stage == "Working"
                            ? WitnessChanges.Resolve(
                                "r1",
                                new ColorWitness(transition.Target.Result))
                            : WitnessChanges.None)
            .Map(MapDelayedOutcome);

    private static WitnessFunctionalRefinementCheck<ConcreteState, AbstractState>
        TerminalCheck(
            StateGraphNode concrete,
            StateGraphNode abstraction)
        => Refinement
            .Between<ConcreteState, AbstractState>(concrete, abstraction)
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => Colors("r1"))
            .Map(MapDelayedOutcome);

    private static RefinementCheckingResult RunLifecycle(
        Func<PendingWitnesses, RefinementTransition<ConcreteState>, WitnessChanges> next,
        int edges)
    {
        var root = ConcreteNode("Idle");
        var current = root;
        for (var index = 0; index < edges; index++)
        {
            var target = ConcreteNode($"Stage{index}");
            AddEdge(current, target, $"step{index}");
            current = target;
        }

        return Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: next)
            .Map((_, _) => new AbstractState("Idle"))
            .Check();
    }

    private static WitnessCollection SingleWitnessCollection()
    {
        WitnessCollection captured = null;
        var root = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(root, working, "start");

        Refinement
            .Between<ConcreteState, AbstractState>(root, AbstractNode("Idle"))
            .WithWitness(
                initial: _ => WitnessChanges.None,
                next: (_, _) => WitnessChanges.Introduce(
                    "r1",
                    new ColorWitness("red")))
            .Map((concrete, witnesses) =>
            {
                if (witnesses.Count > 0)
                {
                    captured = witnesses;
                }
                return new AbstractState("Idle");
            })
            .Check();

        Assert.That(captured, Is.Not.Null);
        return captured;
    }

    private static StateGraphNode BuildConcrete(bool withPoll = false)
    {
        var idle = ConcreteNode("Idle");
        var working = ConcreteNode("Working");
        AddEdge(idle, working, "start");
        if (withPoll)
        {
            AddEdge(working, working, "poll");
        }
        AddEdge(working, ConcreteNode("Done", "red"), "complete");
        AddEdge(working, ConcreteNode("Done", "blue"), "complete");
        return idle;
    }

    private static StateGraphNode BuildAbstract(
        bool blueCanFinish = true,
        bool includeBlue = true)
    {
        var idle = AbstractNode("Idle");
        var red = AbstractNode("Chosen", "red");
        AddEdge(idle, red, "choose");
        AddEdge(red, AbstractNode("Done", "red"), "finish");
        if (includeBlue)
        {
            var blue = AbstractNode("Chosen", "blue");
            AddEdge(idle, blue, "choose");
            if (blueCanFinish)
            {
                AddEdge(blue, AbstractNode("Done", "blue"), "finish");
            }
        }
        return idle;
    }

    private static StateGraphNode ConcreteNode(string stage, string result = null)
        => Node(
            new ConcreteState(stage, result),
            $"concrete-{stage}-{result ?? "-"}");

    private static StateGraphNode AbstractNode(string stage, string choice = null)
        => Node(
            new AbstractState(stage, choice),
            $"abstract-{stage}-{choice ?? "-"}");

    private static StateGraphNode Node(State state, string configuration)
    {
        state.Freeze();
        return new StateGraphNode
        {
            State = state,
            StepFunctions = new IStepFunction[]
            {
                new LabelStep($"configuration-{configuration}")
            },
            Edges = new List<StateGraphEdge>()
        };
    }

    private static void AddEdge(
        StateGraphNode source,
        StateGraphNode target,
        string stepId,
        object metadata = null)
        => source.Edges.Add(new StateGraphEdge
        {
            Target = target,
            StepFunction = new LabelStep(stepId),
            Metadata = metadata
        });
}
