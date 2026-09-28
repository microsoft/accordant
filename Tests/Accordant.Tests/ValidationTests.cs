// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System;
using System.Collections.Generic;
using System.IO.Hashing;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Fail-fast / validation tests. Malformed inputs and broken step-function
/// contracts must be rejected at the boundary with a named exception, rather
/// than surfacing later as an obscure <see cref="NullReferenceException"/> deep
/// inside hashing or fingerprinting.
/// </summary>
[TestFixture]
public class ValidationTests
{
    #region Test states

    private sealed class ProbeState : State
    {
        public int Value { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new ProbeState { Value = this.Value };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => $"probe:{this.Value}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private sealed class OtherProbeState : State
    {
        public int Value { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new OtherProbeState { Value = this.Value };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => $"other:{this.Value}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    // A state whose CloneInternal violates the contract by not registering the
    // clone. Used to prove Clone fails fast instead of throwing KeyNotFoundException.
    private sealed class UnregisteredCloneState : State
    {
        public int Value { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            // Intentionally does NOT set clonedMap[this].
        }

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => $"unregistered:{this.Value}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    #endregion

    #region Test step functions

    // A step function whose Apply returns exactly what the test hands it.
    private sealed class ScriptedStep : BaseStepFunction
    {
        private readonly string id;
        private readonly Func<IState, IList<StepResult>> produce;

        public ScriptedStep(string id, Func<IState, IList<StepResult>> produce)
        {
            this.id = id;
            this.produce = produce;
        }

        public override string StepFunctionId => this.id;

        protected override IList<StepResult> ApplyInternal(IState state)
            => this.produce(state);
    }

    #endregion

    #region StateGraph.ExploreStateGraph

    [Test]
    public void ExploreStateGraph_RejectsNullSteps()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateGraph.ExploreStateGraph(null, new ProbeState()));
        Assert.That(ex.ParamName, Is.EqualTo("steps"));
    }

    [Test]
    public void ExploreStateGraph_RejectsNullStartingState()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateGraph.ExploreStateGraph(Array.Empty<IStepFunction>(), null));
        Assert.That(ex.ParamName, Is.EqualTo("startingState"));
    }

    [Test]
    public void ExploreStateGraph_RejectsNullStepEntry()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            StateGraph.ExploreStateGraph(new IStepFunction[] { null }, new ProbeState()));
        Assert.That(ex.ParamName, Is.EqualTo("steps"));
    }

    [Test]
    public void ExploreStateGraph_RejectsMaxDepthBelowMinusOne()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            StateGraph.ExploreStateGraph(
                Array.Empty<IStepFunction>(),
                new ProbeState(),
                maxDepth: -2));
        Assert.That(ex.ParamName, Is.EqualTo("maxDepth"));
    }

    [Test]
    public void ExploreStateGraph_FailsFast_WhenStepResultStateIsNull()
    {
        var step = new ScriptedStep("null-state", _ =>
            new[] { new StepResult { State = null } });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateGraph.ExploreStateGraph(new IStepFunction[] { step }, new ProbeState()));

        Assert.That(ex.Message, Does.Contain("null-state"));
        Assert.That(ex.Message, Does.Contain("null state"));
    }

    [Test]
    public void ExploreStateGraph_FailsFast_WhenStepResultIsNull()
    {
        var step = new ScriptedStep("null-result", _ =>
            new StepResult[] { null });

        var ex = Assert.Throws<StepFunctionApplicationException>(() =>
            StateGraph.ExploreStateGraph(new IStepFunction[] { step }, new ProbeState()));

        Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(ex.InnerException.Message, Does.Contain("null-result"));
    }

    [Test]
    public void ExploreStateGraph_FailsFast_WhenProducedStepFunctionIsNull()
    {
        var step = new ScriptedStep("null-step", _ =>
            new[]
            {
                new StepResult
                {
                    State = new ProbeState { Value = 1 },
                    StepFunctions = new IStepFunction[] { null },
                },
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateGraph.ExploreStateGraph(new IStepFunction[] { step }, new ProbeState()));

        Assert.That(ex.Message, Does.Contain("null-step"));
        Assert.That(ex.Message, Does.Contain("null step function"));
    }

    [Test]
    public void ExploreStateGraph_AcceptsWellFormedStepResults()
    {
        var step = new ScriptedStep("ok", state =>
            new[]
            {
                new StepResult
                {
                    State = new ProbeState { Value = ((ProbeState)state).Value + 1 },
                    StepFunctions = Array.Empty<IStepFunction>(),
                },
            });

        var root = StateGraph.ExploreStateGraph(
            new IStepFunction[] { step },
            new ProbeState { Value = 0 });

        Assert.That(root.Edges, Has.Count.EqualTo(1));
    }

    #endregion

    #region SystemChecker.Validate

    [Test]
    public void Validate_RejectsNullSequence()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            SystemChecker.Validate(null, new StateProfile(new ProbeState())));
        Assert.That(ex.ParamName, Is.EqualTo("sequenceOfConcurrentSteps"));
    }

    [Test]
    public void Validate_RejectsNullStateProfile()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            SystemChecker.Validate(new List<IList<IStepFunction>>(), (StateProfile)null));
        Assert.That(ex.ParamName, Is.EqualTo("stateProfile"));
    }

    [Test]
    public void Validate_RejectsNullConcurrentGroup()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SystemChecker.Validate(
                new List<IList<IStepFunction>> { null },
                new StateProfile(new ProbeState())));
        Assert.That(ex.ParamName, Is.EqualTo("sequenceOfConcurrentSteps"));
    }

    #endregion

    #region StateProfile

    [Test]
    public void StateProfile_RejectsNullSingleState()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new StateProfile((IState)null));
        Assert.That(ex.ParamName, Is.EqualTo("state"));
    }

    [Test]
    public void StateProfile_RejectsNullStateList()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new StateProfile((IList<IState>)null));
        Assert.That(ex.ParamName, Is.EqualTo("states"));
    }

    [Test]
    public void StateProfile_RejectsNullStateElement()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StateProfile(new IState[] { null }));
        Assert.That(ex.ParamName, Is.EqualTo("states"));
    }

    [Test]
    public void StateProfile_RejectsNullStatesAndStepFunctionsList()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StateProfile((IList<(IState, IList<IStepFunction>)>)null));
        Assert.That(ex.ParamName, Is.EqualTo("statesAndStepFunctions"));
    }

    [Test]
    public void StateProfile_RejectsNullStatesAndStepFunctionsAssignment()
    {
        var profile = new StateProfile(new ProbeState());
        var ex = Assert.Throws<ArgumentNullException>(() => profile.StatesAndStepFunctions = null);
        Assert.That(ex.ParamName, Is.EqualTo("StatesAndStepFunctions"));
    }

    [Test]
    public void StateProfile_SingleState_ThrowsWhenMultipleStates()
    {
        var profile = new StateProfile(
            new List<IState> { new ProbeState { Value = 1 }, new ProbeState { Value = 2 } });

        Assert.That(profile.IsSingleState(), Is.False);
        Assert.Throws<MultipleStateException>(() => profile.SingleState());
    }

    #endregion

    #region ContractStepFunction

    [Test]
    public void ContractStepFunction_FailsFast_WhenVerifyReturnsValidWithoutStateProfile()
    {
        var step = new ContractStepFunction(
            request: Unit.Value,
            observedResponse: Unit.Value,
            verify: (request, state, response) => (true, (StateProfile)null));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            step.Apply(new ProbeState(), Array.Empty<(IStepFunction, StateGraphNode)>()));

        Assert.That(ex.Message, Does.Contain("null StateProfile"));
    }

    #endregion

    #region State and StateGraphNode helpers

    [Test]
    public void State_Clone_RejectsNullMap()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ProbeState().Clone(null));
        Assert.That(ex.ParamName, Is.EqualTo("clonedMap"));
    }

    [Test]
    public void State_Clone_WhenCloneInternalDoesNotRegisterClone_Throws()
    {
        var state = new UnregisteredCloneState { Value = 1 };

        var ex = Assert.Throws<InvalidOperationException>(() => state.Clone());

        Assert.That(ex.Message, Does.Contain("did not register the clone"));
    }

    [Test]
    public void State_Freeze_RejectsNullVisitedSet()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ProbeState().Freeze(null));
        Assert.That(ex.ParamName, Is.EqualTo("visited"));
    }

    [Test]
    public void State_GetStableTypeName_RejectsNullType()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => State.GetStableTypeName(null));
        Assert.That(ex.ParamName, Is.EqualTo("type"));
    }

    [Test]
    public void State_AppendLengthPrefixedString_RejectsNullValue()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            State.AppendLengthPrefixedString(new XxHash64(), null));
        Assert.That(ex.ParamName, Is.EqualTo("value"));
    }

    [Test]
    public void StateGraphNode_GetNodeFingerprint_RejectsNullState()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateGraphNode.GetNodeFingerprint(null, new List<IStepFunction>()));
        Assert.That(ex.ParamName, Is.EqualTo("state"));
    }

    [Test]
    public void StateGraphNode_GetNodeFingerprint_RejectsNullStepFunctions()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateGraphNode.GetNodeFingerprint(new ProbeState(), null));
        Assert.That(ex.ParamName, Is.EqualTo("stepFunctions"));
    }

    [Test]
    public void StateGraphNode_GenerateDotFileContent_RejectsNullRoot()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateGraphNode.GenerateDotFileContent((StateGraphNode)null));
        Assert.That(ex.ParamName, Is.EqualTo("rootNode"));
    }

    #endregion

    #region AsyncOperation type checking

    [Test]
    public void AsyncOperation_FailsFast_OnIncompatibleStateType()
    {
        var operation = AsyncOperation.Create<ProbeState>(
            isTerminal: _ => false,
            transition: state => state.Value++,
            name: "mismatched");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            operation.Apply(
                new OtherProbeState(),
                Array.Empty<(IStepFunction, StateGraphNode)>()));

        Assert.That(ex.Message, Does.Contain("AsyncOperation"));
    }

    #endregion
}
