// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public class FailFastValidationTests
{
    private sealed class ConstantHashState : IState
    {
        public int Value { get; set; }

        public bool IsFrozen { get; private set; }

        public void Freeze()
        {
            IsFrozen = true;
        }

        public IState Clone()
        {
            return new ConstantHashState { Value = Value };
        }

        public ulong GetStateHash() => 42;

        public string StringRepresentation() => Value.ToString();
    }

    private sealed class OtherState : IState
    {
        public bool IsFrozen { get; private set; }

        public void Freeze()
        {
            IsFrozen = true;
        }

        public IState Clone()
        {
            return new OtherState();
        }

        public ulong GetStateHash() => 99;

        public string StringRepresentation() => "other";
    }

    private sealed class ProduceStep : BaseStepFunction
    {
        private readonly string id;
        private readonly int value;
        private readonly IList<IStepFunction> nextStepFunctions;

        public ProduceStep(
            string id,
            int value,
            IList<IStepFunction> nextStepFunctions)
        {
            this.id = id;
            this.value = value;
            this.nextStepFunctions = nextStepFunctions;
        }

        public override string StepFunctionId => id;

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            return new[]
            {
                new StepResult
                {
                    State = new ConstantHashState { Value = value },
                    StepFunctions = nextStepFunctions
                }
            };
        }
    }

    private sealed class BranchStep : BaseStepFunction
    {
        private readonly IStepFunction sharedStep;

        public BranchStep(IStepFunction sharedStep)
        {
            this.sharedStep = sharedStep;
        }

        public override string StepFunctionId => "branch";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            return new[]
            {
                new StepResult
                {
                    State = new ConstantHashState { Value = 1 },
                    StepFunctions = new[] { sharedStep }
                },
                new StepResult
                {
                    State = new ConstantHashState { Value = 2 },
                    StepFunctions = new[] { sharedStep }
                }
            };
        }
    }

    private sealed class ThrowingStep : BaseStepFunction
    {
        public override string StepFunctionId => "throwing";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            throw new InvalidOperationException("expected failure");
        }
    }

    private sealed class CountingStep : BaseStepFunction
    {
        private readonly Action onApply;

        public CountingStep(Action onApply)
        {
            this.onApply = onApply;
        }

        public override string StepFunctionId => "counting";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            onApply();
            return null;
        }
    }

    private sealed class UnfreezableState : IState
    {
        public bool IsFrozen => false;

        public void Freeze()
        {
        }

        public IState Clone() => new UnfreezableState();

        public ulong GetStateHash() => 1;

        public string StringRepresentation() => "unfreezable";
    }

    private sealed class MutatingThrowState : State
    {
        public int Value { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new MutatingThrowState { Value = Value };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
            => Value.ToString();

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class MutatingThrowStep : BaseStepFunction
    {
        public override string StepFunctionId => "mutating-throwing";

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            ((MutatingThrowState)state).Value++;
            throw new InvalidOperationException("expected failure");
        }
    }

    [Test]
    public void StateGraph_DoesNotMergeDistinctStatesWithTheSameHash()
    {
        var sharedStep = new ProduceStep("shared", 3, Array.Empty<IStepFunction>());
        var root = StateGraph.ExploreStateGraph(
            new IStepFunction[] { new BranchStep(sharedStep) },
            new ConstantHashState());

        Assert.That(root.Edges, Has.Count.EqualTo(2));
        Assert.That(root.Edges[0].Target, Is.Not.SameAs(root.Edges[1].Target));
        Assert.That(root.Edges[0].Target.State.StringRepresentation(), Is.Not.EqualTo(
            root.Edges[1].Target.State.StringRepresentation()));
    }

    [Test]
    public void NodeFingerprint_DoesNotUseAmbiguousStepFunctionConcatenation()
    {
        var state = new ConstantHashState();
        var first = new IStepFunction[]
        {
            new ProduceStep("ab", 1, Array.Empty<IStepFunction>()),
            new ProduceStep("c", 1, Array.Empty<IStepFunction>())
        };
        var second = new IStepFunction[]
        {
            new ProduceStep("a", 1, Array.Empty<IStepFunction>()),
            new ProduceStep("bc", 1, Array.Empty<IStepFunction>())
        };

        Assert.That(
            StateGraphNode.GetNodeFingerprint(state, first),
            Is.Not.EqualTo(StateGraphNode.GetNodeFingerprint(state, second)));
    }

    [Test]
    public void AsyncOperation_CopiesTransitionsBeforeUse()
    {
        var transitions = new Action<ConstantHashState>[]
        {
            next => next.Value = 1
        };
        var operation = AsyncOperation.Create<ConstantHashState>(
            isTerminal: _ => false,
            transitions: transitions);

        transitions[0] = null;

        var results = ((IStepFunction)operation).Apply(
            new ConstantHashState(),
            Array.Empty<(IStepFunction, StateGraphNode)>());

        Assert.That(((ConstantHashState)results[0].State).Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncOperation_RejectsNullTransitions()
    {
        Assert.Throws<ArgumentException>(() => AsyncOperation.Create<ConstantHashState>(
            isTerminal: _ => false,
            transitions: new Action<ConstantHashState>[] { null }));
    }

    [Test]
    public void BaseStepFunction_RejectsNullApplyInputs()
    {
        var operation = AsyncOperation.Create<ConstantHashState>(
            isTerminal: _ => false,
            transition: _ => { });

        Assert.Throws<ArgumentNullException>(() => ((IStepFunction)operation).Apply(
            null,
            Array.Empty<(IStepFunction, StateGraphNode)>()));
        Assert.Throws<ArgumentNullException>(() => ((IStepFunction)operation).Apply(
            new ConstantHashState(),
            null));
    }

    [Test]
    public void AsyncOperation_ReportsWrongStateTypeClearly()
    {
        var operation = AsyncOperation.Create<ConstantHashState>(
            isTerminal: _ => false,
            transition: _ => { });

        var exception = Assert.Throws<ArgumentException>(() => ((IStepFunction)operation).Apply(
            new OtherState(),
            Array.Empty<(IStepFunction, StateGraphNode)>()));

        Assert.That(exception.Message, Does.Contain(nameof(ConstantHashState)));
        Assert.That(exception.Message, Does.Contain(nameof(OtherState)));
    }

    [Test]
    public void StepFunctionApplicationException_SnapshotsDiagnostics()
    {
        var exception = Assert.Throws<StepFunctionApplicationException>(() =>
            StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ThrowingStep() },
                new ConstantHashState()));

        Assert.That(typeof(StepFunctionApplicationException)
            .GetProperty(nameof(StepFunctionApplicationException.PathToNode))
            .CanWrite, Is.False);
        Assert.That(typeof(StepFunctionApplicationException)
            .GetProperty(nameof(StepFunctionApplicationException.ExceptionEncounteringNode))
            .CanWrite, Is.False);
        Assert.That(exception.PathToNode, Has.Count.EqualTo(1));

        var mutablePath = exception.PathToNode as IList<(IStepFunction, StateGraphNode)>;
        Assert.That(mutablePath, Is.Not.Null);
        Assert.Throws<NotSupportedException>(() => mutablePath.Clear());
    }

    [Test]
    public void StateProfile_SnapshotsAndProtectsItsCollections()
    {
        var states = new List<IState> { new ConstantHashState() };
        var profile = new StateProfile(states);

        states.Clear();

        Assert.That(profile.StatesAndStepFunctions, Has.Count.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => profile.StatesAndStepFunctions.Clear());
    }

    [Test]
    public void StateGraph_RejectsStatesThatCannotBeFrozen()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StateGraph.ExploreStateGraph(
                Array.Empty<IStepFunction>(),
                new UnfreezableState()));

        Assert.That(exception.Message, Does.Contain("did not become frozen"));
    }

    [Test]
    public void SystemChecker_ValidatesAllBatchesBeforeApplyingAnyStep()
    {
        var applied = false;
        var sequence = new List<IList<IStepFunction>>
        {
            new IStepFunction[] { new CountingStep(() => applied = true) },
            new IStepFunction[] { null }
        };

        Assert.Throws<ArgumentException>(() =>
            SystemChecker.Validate(sequence, new ConstantHashState()));
        Assert.That(applied, Is.False);
    }

    [Test]
    public void BaseStepFunction_ValidatesMutationWhenApplicationThrows()
    {
        Assert.Throws<StateFrozenException>(() =>
            new MutatingThrowStep().Apply(
                new MutatingThrowState(),
                Array.Empty<(IStepFunction, StateGraphNode)>()));
    }

    [Test]
    public void ContractStepFunction_RejectsConfigurationChangesAfterApplication()
    {
        var contract = new ContractStepFunction(
            request: null,
            observedResponse: null,
            verify: (_, _, _) => (false, (StateProfile)null));

        contract.Apply(
            new ConstantHashState(),
            Array.Empty<(IStepFunction, StateGraphNode)>());

        Assert.Throws<InvalidOperationException>(() =>
            contract.SetPredecessorIds(new[] { "predecessor" }));
    }
}
