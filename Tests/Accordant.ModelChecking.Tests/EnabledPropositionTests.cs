// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Bdd;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end tests for the node-level proposition <c>ENABLED A</c>.
    ///
    /// <para>The contract under test:</para>
    /// <list type="bullet">
    ///   <item><c>ENABLED A</c> holds at a graph node iff at least one
    ///   <em>changing</em> outgoing model edge of that node carries an action
    ///   satisfying <c>A</c>.</item>
    ///   <item>State-neutral edges never count, matching Accordant's fairness
    ///   enabledness; a terminal (complete) node therefore enables
    ///   nothing.</item>
    ///   <item>Enabledness is read from the node, not the state: nodes with
    ///   equal states but different active step-function sets can
    ///   disagree.</item>
    ///   <item>An unexpanded / depth-truncated frontier is unknown, so a
    ///   verdict that depends on one is bounded-inconclusive, never
    ///   <c>false</c>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class EnabledPropositionTests
    {
        #region Counter model

        private sealed class CounterState : State
        {
            public int Count { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
                => clonedMap[this] = new CounterState { Count = this.Count };

            protected override string StringRepresentationInternal(
                Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Count={this.Count}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class IncrementStep : BaseStepFunction
        {
            private readonly int max;
            public int ApplyCount;

            public IncrementStep(int max) { this.max = max; }

            public override string StepFunctionId => "Increment";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var cs = (CounterState)state;
                if (cs.Count >= this.max) return null;
                var next = (CounterState)cs.Clone();
                next.Count++;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                        EdgeMetadata = "inc",
                    },
                };
            }
        }

        private sealed class DecrementStep : BaseStepFunction
        {
            public override string StepFunctionId => "Decrement";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var cs = (CounterState)state;
                if (cs.Count <= 0) return null;
                var next = (CounterState)cs.Clone();
                next.Count--;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                        EdgeMetadata = "dec",
                    },
                };
            }
        }

        /// <summary>Produces an edge that leaves the state unchanged.</summary>
        private sealed class NoOpStep : BaseStepFunction
        {
            public override string StepFunctionId => "NoOp";

            protected override IList<StepResult> ApplyInternal(IState state)
                => new[]
                {
                    new StepResult
                    {
                        State = state.Clone(),
                        StepFunctions = new IStepFunction[] { this },
                    },
                };
        }

        private static StateGraphNode IncrementOnly(int max, int maxDepth = -1, bool lazy = false)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max) },
                new CounterState { Count = 0 },
                maxDepth: maxDepth,
                lazy: lazy);

        private static StateGraphNode IncrementAndDecrement(int max)
            => StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(max), new DecrementStep() },
                new CounterState { Count = 0 });

        private static StutterSensitiveFormulaBuilder<CounterState> Sensitive()
            => Formula.For<CounterState>().AllowStutterSensitiveFormulas();

        private static IEnumerable<StateGraphNode> Reachable(StateGraphNode root)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<StateGraphNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!seen.Add(node.GetNodeFingerprint())) continue;
                yield return node;
                foreach (var edge in node.Edges) stack.Push(edge.Target);
            }
        }

        /// <summary>
        /// The SCC engine is only selected when a non-<see cref="Fairness.None"/>
        /// constraint is supplied. This constraint matches no step, so it is
        /// semantically vacuous and only switches the backend.
        /// </summary>
        private static Fairness ForceSccEngine => Fairness.Weak((IStepFunction _) => false);

        #endregion

        #region Basic truth, falsity and terminal nodes

        [Test]
        public void Enabled_HoldsAtANodeWithAMatchingChangingEdge()
        {
            var root = IncrementOnly(max: 3);
            var f = Sensitive();

            Assert.That(root.Check(f.Enabled<IncrementStep>()).Valid, Is.True);
        }

        [Test]
        public void Enabled_IsFalse_WhenNoMatchingActionIsAvailable()
        {
            // At Count = 0 the decrement step is not applicable, so no edge
            // for it exists.
            var root = IncrementAndDecrement(max: 3);
            var f = Sensitive();

            Assert.That(root.Check(f.Enabled<DecrementStep>()).Valid, Is.False);
            Assert.That(root.Check(f.Not(f.Enabled<DecrementStep>())).Valid, Is.True);
        }

        [Test]
        public void Enabled_IsFalse_AtATerminalCompleteNode()
        {
            // max = 0: the root has an increment step attached but it produces
            // no result, so the root is a terminal (complete) node.
            var root = IncrementOnly(max: 0);
            var f = Sensitive();

            Assert.That(root.Edges, Is.Empty);
            Assert.That(root.Check(f.Enabled<IncrementStep>()).Valid, Is.False);
            Assert.That(root.Check(f.Enabled((IStepFunction _) => true)).Valid, Is.False);
        }

        [Test]
        public void Always_Enabled_Fails_AtTheTerminalOfACompleteGraph()
        {
            // Count = 2 is terminal, so "increment is always enabled" is a
            // conclusive violation, not an inconclusive verdict.
            var root = IncrementOnly(max: 2);
            var f = Sensitive();

            var result = root.Check(f.Always(f.Enabled<IncrementStep>()));

            Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.Violated));
            Assert.That(result.Trace, Is.Not.Null);
        }

        [Test]
        public void Eventually_NotEnabled_HoldsBecauseTheChainTerminates()
        {
            var root = IncrementOnly(max: 2);
            var f = Sensitive();

            Assert.That(
                root.Check(f.Eventually(f.Not(f.Enabled<IncrementStep>()))).Valid,
                Is.True);
        }

        #endregion

        #region State-neutral edges are excluded

        [Test]
        public void Enabled_IgnoresStateNeutralEdges()
        {
            // The only outgoing edge is a NoOp self-loop: the node has an
            // edge, so it is not terminal, but nothing is enabled.
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new NoOpStep() },
                new CounterState { Count = 0 });
            var f = Sensitive();

            Assert.That(root.Edges, Is.Not.Empty);
            Assert.That(root.Check(f.Enabled<NoOpStep>()).Valid, Is.False);
            Assert.That(root.Check(f.Enabled((IStepFunction _) => true)).Valid, Is.False);
        }

        [Test]
        public void Enabled_SeesChangingEdgesEvenWhenStateNeutralEdgesArePresent()
        {
            var root = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new IncrementStep(2), new NoOpStep() },
                new CounterState { Count = 0 });
            var f = Sensitive();

            Assert.That(root.Check(f.Enabled<IncrementStep>()).Valid, Is.True);
            Assert.That(root.Check(f.Enabled<NoOpStep>()).Valid, Is.False);
        }

        #endregion

        #region Node-level, not state-level

        /// <summary>
        /// Builds a graph whose first and third node carry <em>equal</em>
        /// states (Count = 0) but different active step-function sets, so
        /// they disagree on enabledness:
        /// <c>a(Count=0, {inc}) --inc--&gt; b(Count=1, {dec}) --dec--&gt;
        /// c(Count=0, {})</c>.
        /// </summary>
        private static StateGraphNode EqualStatesDifferentNodes()
        {
            var increment = new IncrementStep(1);
            var decrement = new DecrementStep();

            var aState = new CounterState { Count = 0 }; aState.Freeze();
            var bState = new CounterState { Count = 1 }; bState.Freeze();
            var cState = new CounterState { Count = 0 }; cState.Freeze();

            var c = new StateGraphNode
            {
                State = cState,
                StepFunctions = new List<IStepFunction>(),
                Edges = new List<StateGraphEdge>(),
            };
            var b = new StateGraphNode
            {
                State = bState,
                StepFunctions = new List<IStepFunction> { decrement },
                Edges = new List<StateGraphEdge>
                {
                    new StateGraphEdge { Target = c, StepFunction = decrement },
                },
            };
            return new StateGraphNode
            {
                State = aState,
                StepFunctions = new List<IStepFunction> { increment },
                Edges = new List<StateGraphEdge>
                {
                    new StateGraphEdge { Target = b, StepFunction = increment },
                },
            };
        }

        [Test]
        public void Enabled_IsNodeLevel_NotStateLevel()
        {
            var root = EqualStatesDifferentNodes();
            var f = Sensitive();
            var enabledIncrement = f.Enabled<IncrementStep>();

            // The first and third positions carry equal states...
            var atZero = f.Observe(s => s.Count == 0, "AtZero");
            Assert.That(root.Check(atZero).Valid, Is.True);
            Assert.That(root.Check(f.Next(f.Next(atZero))).Valid, Is.True);

            // ...yet only the first one enables an increment.
            Assert.That(root.Check(enabledIncrement).Valid, Is.True);
            Assert.That(root.Check(f.Next(f.Next(f.Not(enabledIncrement)))).Valid, Is.True);
            Assert.That(root.Check(f.Always(enabledIncrement)).Valid, Is.False);
        }

        #endregion

        #region Predicate shapes shared with fairness

        [Test]
        public void Enabled_AcceptsTheFairnessActionPredicateShapes()
        {
            var root = IncrementAndDecrement(max: 2);
            var f = Sensitive();
            var safe = Formula.For<CounterState>();
            var incrementEdge = safe.ObserveTransition(
                (state, next) => next.Count == state.Count + 1, "IncrementEdge");

            // Step selector, step type, state relation, full edge predicate
            // and transition observation all describe the same action.
            Assert.That(
                root.Check(f.Enabled(step => step.StepFunctionId == "Increment")).Valid,
                Is.True);
            Assert.That(root.Check(f.Enabled<IncrementStep>()).Valid, Is.True);
            Assert.That(
                root.Check(f.Enabled((state, next) => next.Count == state.Count + 1)).Valid,
                Is.True);
            Assert.That(
                root.Check(f.Enabled(
                    (state, action, next) =>
                        action.StepFunctionId == "Increment" && next.Count > state.Count)).Valid,
                Is.True);
            Assert.That(root.Check(f.Enabled(incrementEdge)).Valid, Is.True);

            // ...and none of them reports the (inapplicable) decrement.
            Assert.That(
                root.Check(f.Enabled(step => step.StepFunctionId == "Decrement")).Valid,
                Is.False);
            Assert.That(
                root.Check(f.Enabled((state, next) => next.Count == state.Count - 1)).Valid,
                Is.False);
        }

        [Test]
        public void Enabled_ReadsTheCandidateEdgeMetadata()
        {
            // The low-level seam sees the whole candidate letter, including
            // the edge metadata the model attached.
            var root = IncrementAndDecrement(max: 2);
            var byMetadata = Ltl<IStatePredicate>.Atom(
                new StatePredAtom(
                    StateProp.Enabled(
                        "Enabled(inc-metadata)",
                        letter => Equals(letter.Metadata, "inc"))));
            var missingMetadata = Ltl<IStatePredicate>.Atom(
                new StatePredAtom(
                    StateProp.Enabled(
                        "Enabled(dec-metadata)",
                        letter => Equals(letter.Metadata, "dec"))));

            Assert.That(SymbolicLtlCheck.Check(root, byMetadata).Valid, Is.True);
            Assert.That(SymbolicLtlCheck.Check(root, missingMetadata).Valid, Is.False);
        }

        [Test]
        public void Enabled_IsNamedForDiagnostics()
        {
            var f = Sensitive();

            Assert.That(f.Enabled<IncrementStep>().ToString(), Does.Contain("IncrementStep"));
            Assert.That(
                f.Enabled((IStepFunction _) => true, "AnyAction").ToString(),
                Is.EqualTo("AnyAction"));
        }

        [Test]
        public void Enabled_IsComputedOncePerNode()
        {
            var root = IncrementAndDecrement(max: 2);
            var evaluations = 0;
            var proposition = StateProp.Enabled(
                "Enabled(counted)",
                _ =>
                {
                    evaluations++;
                    return false;
                });
            var letter = TransitionContext.Edge(
                root.State,
                root.Edges[0].StepFunction,
                root.Edges[0].Metadata,
                root.Edges[0].Target.State,
                root);

            for (var index = 0; index < 10; index++)
            {
                Assert.That(proposition.EvaluateTransition(letter), Is.False);
            }

            Assert.That(
                evaluations,
                Is.EqualTo(root.Edges.Count),
                "the candidate edges are scanned once for this node");
        }

        [Test]
        public void EnabledCannotBeEvaluatedWithoutAGraphNode()
        {
            var proposition = StateProp.Enabled(
                "Enabled(any)",
                _ => true);

            Assert.That(
                () => proposition.Evaluate(new CounterState { Count = 0 }),
                Throws.InvalidOperationException
                    .With.Message.Contains("state-graph node"));
        }

        #endregion

        #region Agreement with fairness enabledness

        [Test]
        public void Enabled_AgreesWithFairnessEnabledness_AtEveryNode()
        {
            // Fairness derives "enabled at v" from v's changing outgoing
            // edges. A single-node SCC with no intra-node edge therefore has
            // "enabled and never taken", i.e. it is weakly unfair for a step
            // exactly when that step is enabled at the node.
            var root = IncrementAndDecrement(max: 3);
            var f = Sensitive();

            foreach (var node in Reachable(root))
            {
                var scc = new StronglyConnectedComponent();
                scc.Nodes.Add(node);

                foreach (var (formula, fairness, label) in new[]
                {
                    ((TemporalFormula)f.Enabled<IncrementStep>(),
                        Fairness.Weak<IncrementStep>(), "Increment"),
                    ((TemporalFormula)f.Enabled<DecrementStep>(),
                        Fairness.Weak<DecrementStep>(), "Decrement"),
                })
                {
                    var byFormula = node.Check(formula).Valid;
                    var byFairness = !fairness.IsFairCycle(scc);
                    Assert.That(
                        byFormula, Is.EqualTo(byFairness),
                        $"{label} at {node.State} " +
                        $"(formula={byFormula}, fairness={byFairness})");
                }
            }
        }

        #endregion

        #region Weak / strong fairness semantics

        private sealed class LoopStep : IStepFunction
        {
            public string StepFunctionId => "Loop";
            public IList<StepResult> Apply(
                IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private sealed class ProgressStep : IStepFunction
        {
            public string StepFunctionId => "Progress";
            public IList<StepResult> Apply(
                IState state, IReadOnlyList<(IStepFunction, StateGraphNode)> path) => null;
        }

        private static StateGraphNode Node(int count, params IStepFunction[] steps)
        {
            var state = new CounterState { Count = count };
            state.Freeze();
            return new StateGraphNode
            {
                State = state,
                StepFunctions = new List<IStepFunction>(steps),
                Edges = new List<StateGraphEdge>(),
            };
        }

        /// <summary>
        /// <c>s0 --loop--&gt; s0</c> (state-neutral) and
        /// <c>s0 --progress--&gt; goal</c>. Progress is continuously enabled
        /// at the only cycle.
        /// </summary>
        private static StateGraphNode ContinuouslyEnabledSystem()
        {
            var loop = new LoopStep();
            var progress = new ProgressStep();
            var s0 = Node(0, loop, progress);
            var goal = Node(99);
            s0.Edges.Add(new StateGraphEdge { Target = s0, StepFunction = loop });
            s0.Edges.Add(new StateGraphEdge { Target = goal, StepFunction = progress });
            return s0;
        }

        /// <summary>
        /// <c>s0 &lt;--loop--&gt; s1</c> and <c>s0 --progress--&gt; goal</c>.
        /// Progress is enabled infinitely often on the cycle but never
        /// continuously.
        /// </summary>
        private static StateGraphNode IntermittentlyEnabledSystem()
        {
            var loop = new LoopStep();
            var progress = new ProgressStep();
            var s0 = Node(0, loop, progress);
            var s1 = Node(1, loop);
            var goal = Node(99);
            s0.Edges.Add(new StateGraphEdge { Target = s1, StepFunction = loop });
            s0.Edges.Add(new StateGraphEdge { Target = goal, StepFunction = progress });
            s1.Edges.Add(new StateGraphEdge { Target = s0, StepFunction = loop });
            return s0;
        }

        [Test]
        public void ContinuousEnabledness_MatchesWeakFairness()
        {
            var s0 = ContinuouslyEnabledSystem();
            var f = Sensitive();
            StutterSafeFormula atGoal = f.Observe(s => s.Count == 99, "AtGoal");
            var enabledProgress = f.Enabled<ProgressStep>();

            // Progress is enabled at every non-goal node, i.e. continuously
            // enabled on the only cycle.
            Assert.That(
                s0.Check(f.Always(f.Implies(f.Not(atGoal), enabledProgress))).Valid,
                Is.True);

            // ...which is exactly what makes weak fairness discharge the
            // self-loop cycle.
            Assert.That(s0.Check(f.Eventually(atGoal)).Valid, Is.False);
            Assert.That(
                s0.Check(f.Eventually(atGoal), fairness: Fairness.Weak<ProgressStep>()).Valid,
                Is.True);

            // The state-neutral loop enables nothing, so weak fairness on it
            // imposes no obligation at all.
            Assert.That(s0.Check(f.Enabled<LoopStep>()).Valid, Is.False);
            Assert.That(
                s0.Check(f.Eventually(atGoal), fairness: Fairness.Weak<LoopStep>()).Valid,
                Is.False);
        }

        [Test]
        public void IntermittentEnabledness_MatchesStrongButNotWeakFairness()
        {
            var s0 = IntermittentlyEnabledSystem();
            var f = Sensitive();
            StutterSafeFormula atGoal = f.Observe(s => s.Count == 99, "AtGoal");
            var enabledProgress = f.Enabled<ProgressStep>();

            // Enabled at s0, disabled at every successor of s0.
            Assert.That(s0.Check(enabledProgress).Valid, Is.True);
            Assert.That(s0.Check(f.Next(f.Not(enabledProgress))).Valid, Is.True);
            Assert.That(s0.Check(f.Always(enabledProgress)).Valid, Is.False);

            // Not continuously enabled -> weak fairness leaves the cycle;
            // enabled infinitely often -> strong fairness removes it.
            Assert.That(
                s0.Check(f.Eventually(atGoal), fairness: Fairness.Weak<ProgressStep>()).Valid,
                Is.False);
            Assert.That(
                s0.Check(f.Eventually(atGoal), fairness: Fairness.Strong<ProgressStep>()).Valid,
                Is.True);
        }

        #endregion

        #region Backend parity

        [Test]
        public void Enabled_AgreesAcrossEmptinessEngines()
        {
            var roots = new[]
            {
                IncrementOnly(max: 3),
                IncrementAndDecrement(max: 3),
                ContinuouslyEnabledSystem(),
                IntermittentlyEnabledSystem(),
            };

            foreach (var root in roots)
            {
                var f = Sensitive();
                var formulas = new TemporalFormula[]
                {
                    f.Enabled((IStepFunction _) => true),
                    f.Always(f.Enabled((IStepFunction _) => true)),
                    f.Eventually(f.Not(f.Enabled((IStepFunction _) => true))),
                    f.Always(f.Eventually(f.Enabled((IStepFunction _) => true))),
                };

                foreach (var formula in formulas)
                {
                    // Nested-DFS engine vs. SCC product engine.
                    var nestedDfs = root.Check(formula);
                    var scc = root.Check(formula, fairness: ForceSccEngine);
                    Assert.That(
                        scc.Valid, Is.EqualTo(nestedDfs.Valid),
                        $"engine parity for {formula} at {root.State}");
                }
            }
        }

        [Test]
        public void Enabled_AgreesAcrossSymbolicAlgebraBackends()
        {
            var root = IncrementAndDecrement(max: 3);
            var f = Sensitive();
            var formulas = new TemporalFormula[]
            {
                f.Enabled<IncrementStep>(),
                f.Enabled<DecrementStep>(),
                f.Always(f.Enabled<IncrementStep>()),
                f.Eventually(f.Enabled<DecrementStep>()),
            };

            var withFallback = formulas.Select(phi => root.Check(phi).Valid).ToList();

            try
            {
                StatePropEbaProvider.SetDefault(BddStatePropEba.Instance);
                var withBdd = formulas.Select(phi => root.Check(phi).Valid).ToList();
                Assert.That(withBdd, Is.EqualTo(withFallback));
            }
            finally
            {
                StatePropEbaProvider.ResetToFallback();
            }
        }

        [Test]
        public void Enabled_AgreesAcrossTheLtlProductEvaluators()
        {
            // The same node-level proposition, checked through the LTL
            // pipelines: Tarjan product exploration and nested DFS.
            var root = IncrementOnly(max: 3);
            var enabled = Ltl<IStatePredicate>.Atom(
                new StatePredAtom(
                    StateProp.Enabled(
                        "Enabled(Increment)",
                        letter => letter.Action?.StepFunctionId == "Increment")));

            foreach (var phi in new[]
            {
                enabled,
                Ltl<IStatePredicate>.Globally(enabled),
                Ltl<IStatePredicate>.Eventually(LtlAlgebra.Default.Not(enabled)),
            })
            {
                var tarjan = SymbolicLtlCheck.Check(root, phi);
                var ndfs = SymbolicLtlCheck.CheckNDFS(root, phi);
                Assert.That(ndfs.Valid, Is.EqualTo(tarjan.Valid), phi.ToString());
            }

            Assert.That(SymbolicLtlCheck.Check(root, enabled).Valid, Is.True);
            Assert.That(
                SymbolicLtlCheck.Check(root, Ltl<IStatePredicate>.Globally(enabled)).Valid,
                Is.False);
        }

        #endregion

        #region Ordinary propositions are preserved

        [Test]
        public void StateAndNodePropositions_Combine()
        {
            var root = IncrementOnly(max: 2);
            var f = Sensitive();
            StutterSafeFormula belowMax = f.Observe(s => s.Count < 2, "BelowMax");
            var enabledIncrement = f.Enabled<IncrementStep>();

            // Increment is enabled exactly at the non-maximal counts.
            Assert.That(
                root.Check(f.Always(f.Implies(belowMax, enabledIncrement))).Valid, Is.True);
            Assert.That(
                root.Check(f.Always(f.Implies(enabledIncrement, belowMax))).Valid, Is.True);

            // Plain state propositions are unaffected.
            Assert.That(root.Check(f.Always(f.Observe(s => s.Count >= 0))).Valid, Is.True);
        }

        #endregion

        #region Lazy exploration

        /// <summary>
        /// Moves the counter to the absorbing "bad" value -1, where it then
        /// self-loops without changing the state. Its id sorts before
        /// <see cref="GrowStep"/> so it is the first edge explored.
        /// </summary>
        private sealed class ToBadStep : BaseStepFunction
        {
            public override string StepFunctionId => "A_ToBad";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var next = (CounterState)state.Clone();
                next.Count = -1;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                    },
                };
            }
        }

        /// <summary>Grows the counter, but only outside the bad value.</summary>
        private sealed class GrowStep : BaseStepFunction
        {
            private readonly int max;
            public int ApplyCount;

            public GrowStep(int max) { this.max = max; }

            public override string StepFunctionId => "B_Grow";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                this.ApplyCount++;
                var cs = (CounterState)state;
                if (cs.Count < 0 || cs.Count >= this.max) return null;
                var next = (CounterState)cs.Clone();
                next.Count++;
                return new[]
                {
                    new StepResult
                    {
                        State = next,
                        StepFunctions = new IStepFunction[] { this },
                    },
                };
            }
        }

        [Test]
        public void Enabled_AgreesBetweenLazyAndEagerGraphs()
        {
            const int max = 3;
            var eager = IncrementOnly(max);
            var lazy = IncrementOnly(max, lazy: true);
            var f = Sensitive();

            var formulas = new TemporalFormula[]
            {
                f.Enabled<IncrementStep>(),
                f.Always(f.Enabled<IncrementStep>()),
                f.Eventually(f.Not(f.Enabled<IncrementStep>())),
            };

            foreach (var formula in formulas)
            {
                Assert.That(
                    lazy.Check(formula).Valid,
                    Is.EqualTo(eager.Check(formula).Valid),
                    formula.ToString());
            }
        }

        [Test]
        public void Enabled_DoesNotForceExpansionBeyondTheWalkedGraph()
        {
            const int max = 500;

            // "A_ToBad" sorts before "B_Grow", so the lazy search reaches the
            // absorbing bad node (which enables nothing) after two nodes and
            // closes the lasso there, without descending the long grow chain.
            var eagerGrow = new GrowStep(max);
            var eager = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ToBadStep(), eagerGrow },
                new CounterState { Count = 0 });

            var lazyGrow = new GrowStep(max);
            var lazy = StateGraph.ExploreStateGraph(
                new IStepFunction[] { new ToBadStep(), lazyGrow },
                new CounterState { Count = 0 },
                lazy: true);

            var f = Sensitive();
            var formula = f.Always(f.Enabled<GrowStep>());

            Assert.That(eager.Check(formula).Valid, Is.False);
            Assert.That(lazy.Check(formula).Valid, Is.False);

            Assert.That(eagerGrow.ApplyCount, Is.GreaterThan(max));
            Assert.That(
                lazyGrow.ApplyCount, Is.LessThan(eagerGrow.ApplyCount),
                "evaluating ENABLED must not expand the whole graph");
            Assert.That(
                lazyGrow.ApplyCount, Is.LessThan(20),
                "the lazy search should only touch a shallow prefix");
        }

        #endregion

        #region Bounded frontiers are unknown, not false

        [Test]
        public void Enabled_IsInconclusive_AtAConstructionTimeDepthFrontier()
        {
            // The chain is truncated at depth 3; the frontier node's outgoing
            // edges were never generated, so "increment is always enabled"
            // cannot be decided.
            var bounded = IncrementOnly(max: 100, maxDepth: 3);
            var f = Sensitive();

            var result = bounded.Check(f.Always(f.Enabled<IncrementStep>()));

            Assert.That(result.Status, Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
            Assert.That(result.Valid, Is.Null);
        }

        [Test]
        public void Enabled_IsInconclusive_AtALazyConstructionTimeDepthFrontier()
        {
            var bounded = IncrementOnly(max: 100, maxDepth: 3, lazy: true);
            var f = Sensitive();

            Assert.That(
                bounded.Check(f.Always(f.Enabled<IncrementStep>())).Status,
                Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
        }

        [Test]
        public void Enabled_IsInconclusive_AtACheckTimeDepthBound()
        {
            var root = IncrementOnly(max: 100, maxDepth: 6);
            var f = Sensitive();

            Assert.That(
                root.Check(f.Always(f.Enabled<IncrementStep>()), maxDepth: 2).Status,
                Is.EqualTo(PropertyCheckingStatus.InconclusiveBound));
        }

        [Test]
        public void Enabled_StaysConclusive_WhenTheFrontierCannotAffectTheVerdict()
        {
            // Increment is enabled at the root, which the bound cannot change.
            var bounded = IncrementOnly(max: 100, maxDepth: 3);
            var f = Sensitive();

            Assert.That(bounded.Check(f.Enabled<IncrementStep>()).Valid, Is.True);
        }

        #endregion

        #region Surface area and diagnostics

        [Test]
        public void Enabled_IsNotOfferedByTheStutterSafeBuilder()
        {
            var safeMethods = typeof(FormulaBuilder<CounterState>)
                .GetMethods()
                .Select(method => method.Name)
                .ToHashSet();
            var sensitiveMethods = typeof(StutterSensitiveFormulaBuilder<CounterState>)
                .GetMethods()
                .Where(method => method.DeclaringType
                    == typeof(StutterSensitiveFormulaBuilder<CounterState>))
                .Select(method => method.Name)
                .ToHashSet();

            Assert.That(safeMethods, Does.Not.Contain("Enabled"));
            Assert.That(sensitiveMethods, Does.Contain("Enabled"));
        }

        [Test]
        public void Enabled_AppearsInCounterexampleValuations()
        {
            var root = IncrementOnly(max: 2);
            var f = Sensitive();

            var result = root.Check(f.Always(f.Enabled<IncrementStep>()));

            Assert.That(result.Valid, Is.False);

            bool NotEnabledAt(TraceItem item)
            {
                Assert.That(item.Valuation, Is.Not.Null);
                var entry = item.Valuation.Single(
                    kv => kv.Key.ToString().Contains("Enabled(IncrementStep)"));
                // The registered guard is the negation ¬ENABLED, so its value
                // is "nothing enabled here".
                return entry.Value;
            }

            Assert.That(NotEnabledAt(result.Trace.First()), Is.False,
                "increment is enabled at the root");
            Assert.That(NotEnabledAt(result.Trace.Last()), Is.True,
                "the terminal node enables nothing");
        }

        #endregion
    }
}
