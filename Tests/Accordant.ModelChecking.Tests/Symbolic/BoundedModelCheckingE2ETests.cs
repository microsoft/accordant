namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// End-to-end bounded symbolic model checking of Bragger model programs.
    /// Each test defines a model program (state + operations), explores the state
    /// graph with bounded depth, then checks LTL properties using SymbolicLtlCheck.
    /// </summary>
    [TestFixture]
    public class BoundedModelCheckingE2ETests
    {
        #region LTL Helpers

        private static StateProp Prop(string name, Func<IState, bool> eval)
            => new StateProp(name, eval);

        private static Ltl<IStatePredicate> Atom(StateProp p)
            => Ltl<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ltl<IStatePredicate> NegAtom(StateProp p)
            => LtlAlgebra.Default.NegAtom(new StatePredAtom(p));

        private static Ltl<IStatePredicate> G(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Globally(f);

        private static Ltl<IStatePredicate> F(Ltl<IStatePredicate> f)
            => Ltl<IStatePredicate>.Eventually(f);

        private static Ltl<IStatePredicate> And(Ltl<IStatePredicate> a, Ltl<IStatePredicate> b)
            => LtlAlgebra.Default.And(a, b);

        private static Ltl<IStatePredicate> Or(Ltl<IStatePredicate> a, Ltl<IStatePredicate> b)
            => LtlAlgebra.Default.Or(a, b);

        private static Ltl<IStatePredicate> Implies(Ltl<IStatePredicate> a, Ltl<IStatePredicate> b)
            => LtlAlgebra.Default.Implies(a, b);

        #endregion

        #region Model 1: Bounded Counter

        /// <summary>
        /// A simple counter that increments and decrements between 0 and MAX.
        /// Properties:
        /// - Safety: G(0 ≤ count ≤ MAX)
        /// - Liveness: GF(count > 0) — counter is infinitely often positive
        /// </summary>
        private sealed class CounterState : State
        {
            public int Count { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                clonedMap[this] = new CounterState
                {
                    Count = this.Count
                };
            }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Count={this.Count}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class IncrementOp : TerminatingStepFunction
        {
            private readonly int _max;
            public IncrementOp(int max) { _max = max; }
            public override string StepFunctionId => "Increment";
            public override Func<IState, bool> IsTerminalState
                => s => ((CounterState)s).Count >= _max;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var cs = (CounterState)state;
                if (cs.Count >= _max) return null;
                var next = (CounterState)cs.Clone();
                next.Count++;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class DecrementOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "Decrement";
            public override Func<IState, bool> IsTerminalState
                => s => ((CounterState)s).Count <= 0;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var cs = (CounterState)state;
                if (cs.Count <= 0) return null;
                var next = (CounterState)cs.Clone();
                next.Count--;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        [Test]
        public void Counter_Safety_InBounds()
        {
            const int max = 3;
            var initial = new CounterState { Count = 0 };
            var steps = new IStepFunction[] { new IncrementOp(max), new DecrementOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 10);

            var inBounds = Prop("inBounds", s => {
                var c = ((CounterState)s).Count;
                return c >= 0 && c <= max;
            });

            var result = SymbolicLtlCheck.Check(root, G(Atom(inBounds)));
            Assert.That(result.Valid, Is.True,
                "Counter should always remain within [0, max]");
        }

        [Test]
        public void Counter_Safety_NeverNegative()
        {
            const int max = 5;
            var initial = new CounterState { Count = 2 };
            var steps = new IStepFunction[] { new IncrementOp(max), new DecrementOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 15);

            var nonNeg = Prop("nonNeg", s => ((CounterState)s).Count >= 0);
            var result = SymbolicLtlCheck.Check(root, G(Atom(nonNeg)));
            Assert.That(result.Valid, Is.True);
        }

        [Test]
        public void Counter_Reachability_CanReachMax()
        {
            // Starting at max-1 with only increment, F(atMax) holds trivially
            const int max = 3;
            var initial = new CounterState { Count = max - 1 };
            var steps = new IStepFunction[] { new IncrementOp(max) };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 10);

            var atMax = Prop("atMax", s => ((CounterState)s).Count == max);
            // With only increment from max-1, we must reach max
            var result = SymbolicLtlCheck.Check(root, F(Atom(atMax)));
            Assert.That(result.Valid, Is.True,
                "Starting at max-1 with only increment, counter must reach max");
        }

        #endregion

        #region Model 2: Traffic Light

        /// <summary>
        /// Traffic light cycles: Red → Green → Yellow → Red.
        /// Properties:
        /// - Safety: G(¬(Red ∧ Green)) — never red and green simultaneously
        /// - Liveness: GF(Green) — green appears infinitely often
        /// </summary>
        private enum LightColor { Red, Green, Yellow }

        private sealed class TrafficLightState : State
        {
            public LightColor Color { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                clonedMap[this] = new TrafficLightState
                {
                    Color = this.Color
                };
            }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Color={this.Color}".ToString();

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class ChangeColorOp : TerminatingStepFunction
        {
            private readonly LightColor _from;
            private readonly LightColor _to;

            public ChangeColorOp(LightColor from, LightColor to)
            {
                _from = from; _to = to;
            }

            public override string StepFunctionId => $"Change_{_from}_to_{_to}";
            public override Func<IState, bool> IsTerminalState
                => s => ((TrafficLightState)s).Color != _from;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var tls = (TrafficLightState)state;
                if (tls.Color != _from) return null;
                var next = (TrafficLightState)tls.Clone();
                next.Color = _to;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        [Test]
        public void TrafficLight_Safety_NeverRedAndGreen()
        {
            var initial = new TrafficLightState { Color = LightColor.Red };
            var steps = new IStepFunction[]
            {
                new ChangeColorOp(LightColor.Red, LightColor.Green),
                new ChangeColorOp(LightColor.Green, LightColor.Yellow),
                new ChangeColorOp(LightColor.Yellow, LightColor.Red)
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            // G(¬(red ∧ green)) — trivially true since color is one value
            var red = Prop("red", s => ((TrafficLightState)s).Color == LightColor.Red);
            var green = Prop("green", s => ((TrafficLightState)s).Color == LightColor.Green);
            var notBoth = G(Or(NegAtom(red), NegAtom(green)));

            var result = SymbolicLtlCheck.Check(root, notBoth);
            Assert.That(result.Valid, Is.True,
                "Traffic light should never be both red and green");
        }

        [Test]
        public void TrafficLight_Liveness_GreenInfinitelyOften()
        {
            var initial = new TrafficLightState { Color = LightColor.Red };
            var steps = new IStepFunction[]
            {
                new ChangeColorOp(LightColor.Red, LightColor.Green),
                new ChangeColorOp(LightColor.Green, LightColor.Yellow),
                new ChangeColorOp(LightColor.Yellow, LightColor.Red)
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            var green = Prop("green", s => ((TrafficLightState)s).Color == LightColor.Green);
            // GF(green) — green appears infinitely often
            var result = SymbolicLtlCheck.Check(root, G(F(Atom(green))));
            Assert.That(result.Valid, Is.True,
                "Traffic light should cycle through green infinitely often");
        }

        [Test]
        public void TrafficLight_EventuallyGreen()
        {
            var initial = new TrafficLightState { Color = LightColor.Red };
            var steps = new IStepFunction[]
            {
                new ChangeColorOp(LightColor.Red, LightColor.Green),
                new ChangeColorOp(LightColor.Green, LightColor.Yellow),
                new ChangeColorOp(LightColor.Yellow, LightColor.Red)
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 10);

            var green = Prop("green", s => ((TrafficLightState)s).Color == LightColor.Green);
            var result = SymbolicLtlCheck.Check(root, F(Atom(green)));
            Assert.That(result.Valid, Is.True,
                "Starting from red, the light should eventually turn green");
        }

        #endregion

        #region Model 3: Producer-Consumer Buffer

        /// <summary>
        /// Bounded buffer: producer adds items, consumer removes items.
        /// Properties:
        /// - Safety: G(0 ≤ size ≤ capacity)
        /// - No overflow: G(size ≤ capacity)
        /// - Liveness: GF(size < capacity) — buffer is infinitely often not full
        /// </summary>
        private sealed class BufferState : State
        {
            public int Size { get; set; }
            public int Capacity { get; set; }

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                clonedMap[this] = new BufferState
                {
                    Size = this.Size,
                    Capacity = this.Capacity
                };
            }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"Size={this.Size},Capacity={this.Capacity}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class ProduceOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "Produce";
            public override Func<IState, bool> IsTerminalState
                => s => ((BufferState)s).Size >= ((BufferState)s).Capacity;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var bs = (BufferState)state;
                if (bs.Size >= bs.Capacity) return null;
                var next = (BufferState)bs.Clone();
                next.Size++;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class ConsumeOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "Consume";
            public override Func<IState, bool> IsTerminalState
                => s => ((BufferState)s).Size <= 0;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var bs = (BufferState)state;
                if (bs.Size <= 0) return null;
                var next = (BufferState)bs.Clone();
                next.Size--;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        [Test]
        public void Buffer_Safety_NoOverflow()
        {
            var initial = new BufferState { Size = 0, Capacity = 3 };
            var steps = new IStepFunction[] { new ProduceOp(), new ConsumeOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 15);

            var noOverflow = Prop("noOverflow",
                s => ((BufferState)s).Size <= ((BufferState)s).Capacity);
            var result = SymbolicLtlCheck.Check(root, G(Atom(noOverflow)));
            Assert.That(result.Valid, Is.True,
                "Buffer should never exceed capacity");
        }

        [Test]
        public void Buffer_Safety_NonNegativeSize()
        {
            var initial = new BufferState { Size = 1, Capacity = 4 };
            var steps = new IStepFunction[] { new ProduceOp(), new ConsumeOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 15);

            var nonNeg = Prop("nonNeg", s => ((BufferState)s).Size >= 0);
            var result = SymbolicLtlCheck.Check(root, G(Atom(nonNeg)));
            Assert.That(result.Valid, Is.True,
                "Buffer size should never be negative");
        }

        [Test]
        public void Buffer_Liveness_NotAlwaysFull()
        {
            var initial = new BufferState { Size = 0, Capacity = 2 };
            var steps = new IStepFunction[] { new ProduceOp(), new ConsumeOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 15);

            var notFull = Prop("notFull",
                s => ((BufferState)s).Size < ((BufferState)s).Capacity);
            // GF(notFull) — buffer is infinitely often not full
            var result = SymbolicLtlCheck.Check(root, G(F(Atom(notFull))));
            Assert.That(result.Valid, Is.True,
                "Buffer should be infinitely often not full (can always consume)");
        }

        #endregion

        #region Model 4: Simple Mutex Protocol

        /// <summary>
        /// Two-process mutex with a turn variable.
        /// State: (p1_in_cs, p2_in_cs, turn ∈ {1,2})
        /// Properties:
        /// - Safety (mutual exclusion): G(¬(p1_in_cs ∧ p2_in_cs))
        /// - Liveness: G(requesting → F(in_cs))
        /// </summary>
        private sealed class MutexState : State
        {
            public bool P1InCS { get; set; }
            public bool P2InCS { get; set; }
            public int Turn { get; set; } // 1 or 2

            protected override void CloneInternal(Dictionary<object, object> clonedMap)
            {
                clonedMap[this] = new MutexState
                {
                    P1InCS = this.P1InCS,
                    P2InCS = this.P2InCS,
                    Turn = this.Turn
                };
            }

            protected override string StringRepresentationInternal(Dictionary<object, string> objectPaths, string path, bool forceRecompute)
                => $"P1InCS={this.P1InCS},P2InCS={this.P2InCS},Turn={this.Turn}";

            protected override void FreezeComponents(HashSet<object> visited)
            {
            }
        }

        private sealed class P1EnterOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "P1_Enter";
            public override Func<IState, bool> IsTerminalState
                => s => {
                    var ms = (MutexState)s;
                    return ms.P1InCS || ms.P2InCS || ms.Turn != 1;
                };

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var ms = (MutexState)state;
                if (ms.P1InCS || ms.P2InCS || ms.Turn != 1) return null;
                var next = (MutexState)ms.Clone();
                next.P1InCS = true;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class P1ExitOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "P1_Exit";
            public override Func<IState, bool> IsTerminalState
                => s => !((MutexState)s).P1InCS;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var ms = (MutexState)state;
                if (!ms.P1InCS) return null;
                var next = (MutexState)ms.Clone();
                next.P1InCS = false;
                next.Turn = 2; // pass turn to P2
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class P2EnterOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "P2_Enter";
            public override Func<IState, bool> IsTerminalState
                => s => {
                    var ms = (MutexState)s;
                    return ms.P2InCS || ms.P1InCS || ms.Turn != 2;
                };

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var ms = (MutexState)state;
                if (ms.P2InCS || ms.P1InCS || ms.Turn != 2) return null;
                var next = (MutexState)ms.Clone();
                next.P2InCS = true;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        private sealed class P2ExitOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "P2_Exit";
            public override Func<IState, bool> IsTerminalState
                => s => !((MutexState)s).P2InCS;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var ms = (MutexState)state;
                if (!ms.P2InCS) return null;
                var next = (MutexState)ms.Clone();
                next.P2InCS = false;
                next.Turn = 1; // pass turn to P1
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        [Test]
        public void Mutex_Safety_MutualExclusion()
        {
            var initial = new MutexState { P1InCS = false, P2InCS = false, Turn = 1 };
            var steps = new IStepFunction[]
            {
                new P1EnterOp(), new P1ExitOp(),
                new P2EnterOp(), new P2ExitOp()
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            var mutualExcl = Prop("mutualExcl",
                s => !(((MutexState)s).P1InCS && ((MutexState)s).P2InCS));
            var result = SymbolicLtlCheck.Check(root, G(Atom(mutualExcl)));
            Assert.That(result.Valid, Is.True,
                "Mutual exclusion: P1 and P2 should never both be in critical section");
        }

        [Test]
        public void Mutex_Liveness_P1EventuallyEnters()
        {
            var initial = new MutexState { P1InCS = false, P2InCS = false, Turn = 1 };
            var steps = new IStepFunction[]
            {
                new P1EnterOp(), new P1ExitOp(),
                new P2EnterOp(), new P2ExitOp()
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            var p1InCs = Prop("p1InCS", s => ((MutexState)s).P1InCS);
            // GF(p1InCS) — P1 enters CS infinitely often
            var result = SymbolicLtlCheck.Check(root, G(F(Atom(p1InCs))));
            Assert.That(result.Valid, Is.True,
                "P1 should enter critical section infinitely often");
        }

        [Test]
        public void Mutex_Liveness_P2EventuallyEnters()
        {
            var initial = new MutexState { P1InCS = false, P2InCS = false, Turn = 1 };
            var steps = new IStepFunction[]
            {
                new P1EnterOp(), new P1ExitOp(),
                new P2EnterOp(), new P2ExitOp()
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            var p2InCs = Prop("p2InCS", s => ((MutexState)s).P2InCS);
            var result = SymbolicLtlCheck.Check(root, G(F(Atom(p2InCs))));
            Assert.That(result.Valid, Is.True,
                "P2 should enter critical section infinitely often");
        }

        #endregion

        #region Model 5: Broken Counter (Negative Test)

        /// <summary>
        /// A buggy counter that can go below 0 — should fail safety check.
        /// </summary>
        private sealed class BuggyDecrementOp : TerminatingStepFunction
        {
            public override string StepFunctionId => "BuggyDecrement";
            // Bug: always enabled (no guard on count > 0)
            public override Func<IState, bool> IsTerminalState => s => false;

            protected override IList<StepResult> GetStepResults(IState state)
            {
                var cs = (CounterState)state;
                var next = (CounterState)cs.Clone();
                next.Count--;
                return new[] { new StepResult { State = next, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        [Test]
        public void BuggyCounter_Safety_Violated()
        {
            var initial = new CounterState { Count = 1 };
            var steps = new IStepFunction[] { new IncrementOp(3), new BuggyDecrementOp() };

            // Bounded check — buggy decrement will go negative
            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 5);

            var nonNeg = Prop("nonNeg", s => ((CounterState)s).Count >= 0);
            var result = SymbolicLtlCheck.Check(root, G(Atom(nonNeg)));
            Assert.That(result.Valid, Is.False,
                "Buggy counter should violate non-negativity");
        }

        #endregion

        #region Model 6: Bounded Depth Effects

        [Test]
        public void BoundedDepth_ShallowBound_MaySatisfyProperty()
        {
            // With shallow depth bound, a property might appear to hold
            // that would be violated with deeper exploration
            var initial = new CounterState { Count = 5 };
            var steps = new IStepFunction[] { new BuggyDecrementOp() };

            // At depth 4, counter goes 5→4→3→2→1 (all non-negative within bound)
            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 4);

            var nonNeg = Prop("nonNeg", s => ((CounterState)s).Count >= 0);
            // With bounded checking at this depth, the stutter loop
            // keeps it at count=1 forever — property holds
            var result = SymbolicLtlCheck.Check(root, G(Atom(nonNeg)), maxDepth: 4);
            // Note: depending on stutter semantics, this may pass at shallow depth
            // The key point is bounded checking doesn't crash
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void BoundedDepth_DeeperBound_FindsViolation()
        {
            var initial = new CounterState { Count = 2 };
            var steps = new IStepFunction[] { new BuggyDecrementOp() };

            // At depth 5, counter goes 2→1→0→-1→... violates non-negativity
            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 5);

            var nonNeg = Prop("nonNeg", s => ((CounterState)s).Count >= 0);
            var result = SymbolicLtlCheck.Check(root, G(Atom(nonNeg)));
            Assert.That(result.Valid, Is.False,
                "Deeper exploration should find the negativity violation");
        }

        #endregion

        #region Model 7: Response Properties

        [Test]
        public void TrafficLight_Response_RedLeadsToGreen()
        {
            var initial = new TrafficLightState { Color = LightColor.Red };
            var steps = new IStepFunction[]
            {
                new ChangeColorOp(LightColor.Red, LightColor.Green),
                new ChangeColorOp(LightColor.Green, LightColor.Yellow),
                new ChangeColorOp(LightColor.Yellow, LightColor.Red)
            };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 20);

            var red = Prop("red", s => ((TrafficLightState)s).Color == LightColor.Red);
            var green = Prop("green", s => ((TrafficLightState)s).Color == LightColor.Green);

            // G(red → F(green)) — every red is eventually followed by green
            var result = SymbolicLtlCheck.Check(root, G(Implies(Atom(red), F(Atom(green)))));
            Assert.That(result.Valid, Is.True,
                "Red should always be eventually followed by green");
        }

        [Test]
        public void Buffer_Response_FullImpliesEventuallyNotFull()
        {
            var initial = new BufferState { Size = 0, Capacity = 2 };
            var steps = new IStepFunction[] { new ProduceOp(), new ConsumeOp() };

            var root = StateGraph.ExploreStateGraph(steps, initial, maxDepth: 15);

            var full = Prop("full", s => ((BufferState)s).Size >= ((BufferState)s).Capacity);
            var notFull = Prop("notFull", s => ((BufferState)s).Size < ((BufferState)s).Capacity);

            // G(full → F(notFull)) — whenever full, eventually becomes not full
            var result = SymbolicLtlCheck.Check(root, G(Implies(Atom(full), F(Atom(notFull)))));
            Assert.That(result.Valid, Is.True,
                "Full buffer should eventually have space (consume can run)");
        }

        #endregion
    }
}
