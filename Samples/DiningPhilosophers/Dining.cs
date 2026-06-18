namespace DiningPhilosophers
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Three-philosopher dining table. Each philosopher repeats the cycle
    /// <code>
    ///   Thinking → Hungry → HoldOne → Eating → Thinking
    /// </code>
    /// and shares two forks with neighbours. Two pickup orders are
    /// supported:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Naive</b> — every philosopher picks up the left fork first.
    /// Admits the textbook deadlock: all three pick up their left fork
    /// simultaneously, then each waits for its right fork forever.
    /// </description></item>
    /// <item><description>
    /// <b>Asymmetric</b> — philosopher 2 picks up the right fork first
    /// (Chandy/Misra-style ordering). Breaks the circular wait so the
    /// system is deadlock-free.
    /// </description></item>
    /// </list>
    /// A single <see cref="DeadlockStutterStep"/> is included to give the
    /// classical "all-stuck" state an outgoing self-loop, so the
    /// cycle-detection machinery can flag the absence of progress.
    /// </summary>
    public static class Dining
    {
        public const int N = 3;

        // --- Atomic predicates --------------------------------------------

        public static bool Eating0(IState s) => ((DiningState)s).PC0 == PhilPC.Eating;
        public static bool Eating1(IState s) => ((DiningState)s).PC1 == PhilPC.Eating;
        public static bool Eating2(IState s) => ((DiningState)s).PC2 == PhilPC.Eating;
        public static bool Hungry0(IState s) => ((DiningState)s).PC0 == PhilPC.Hungry;
        public static bool Hungry1(IState s) => ((DiningState)s).PC1 == PhilPC.Hungry;
        public static bool Hungry2(IState s) => ((DiningState)s).PC2 == PhilPC.Hungry;

        /// <summary>At least one philosopher is eating.</summary>
        public static bool SomeEating(IState s)
            => Eating0(s) || Eating1(s) || Eating2(s);

        /// <summary>Two or more philosophers are eating simultaneously — should never happen.</summary>
        public static bool TwoEating(IState s)
        {
            var st = (DiningState)s;
            int n = 0;
            if (st.PC0 == PhilPC.Eating) n++;
            if (st.PC1 == PhilPC.Eating) n++;
            if (st.PC2 == PhilPC.Eating) n++;
            return n >= 2;
        }

        // --- State-graph construction ------------------------------------

        /// <summary>Initial state: everyone thinking, every fork free.</summary>
        public static DiningState InitialState() => new DiningState
        {
            PC0 = PhilPC.Thinking, PC1 = PhilPC.Thinking, PC2 = PhilPC.Thinking,
            F0 = -1, F1 = -1, F2 = -1,
        };

        /// <summary>
        /// Build the step list for either the <paramref name="asymmetric"/>
        /// or naive pickup order. Each philosopher contributes the same
        /// four steps; only the <c>firstFork</c>/<c>secondFork</c>
        /// arguments differ between variants.
        /// </summary>
        public static IList<IStepFunction> AllSteps(bool asymmetric)
        {
            var steps = new List<IStepFunction>();
            for (int i = 0; i < N; i++)
            {
                int leftFork = i;
                int rightFork = (i + 1) % N;
                int first, second;
                if (asymmetric && i == N - 1)
                {
                    // Reverse pickup order for the last philosopher.
                    first = rightFork; second = leftFork;
                }
                else
                {
                    first = leftFork; second = rightFork;
                }
                steps.Add(new BecomeHungryStep(i));
                steps.Add(new PickupFirstStep(i, first));
                steps.Add(new PickupSecondStep(i, second));
                steps.Add(new ReleaseStep(i, first, second));
            }
            steps.Add(new DeadlockStutterStep());
            return steps;
        }

        // --- Step functions ----------------------------------------------

        /// <summary>Common per-philosopher step scaffolding.</summary>
        public abstract class PhilStep : BaseStepFunction
        {
            public int I { get; }
            protected PhilStep(int i) { I = i; }

            public override string StepFunctionId => GetType().Name + "_" + I;
            public abstract bool IsEnabled(DiningState s);
            public abstract DiningState Apply(DiningState s);

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var s = (DiningState)state;
                if (!IsEnabled(s)) return null;
                return new[]
                {
                    new StepResult
                    {
                        State = Apply(s),
                        StepFunctions = new IStepFunction[] { this },
                    }
                };
            }

            // Helpers for reading/writing a philosopher's PC and a fork by index.
            protected static PhilPC GetPC(DiningState s, int i)
                => i == 0 ? s.PC0 : i == 1 ? s.PC1 : s.PC2;
            protected static void SetPC(DiningState s, int i, PhilPC pc)
            {
                if (i == 0) s.PC0 = pc;
                else if (i == 1) s.PC1 = pc;
                else s.PC2 = pc;
            }
            protected static int GetFork(DiningState s, int f)
                => f == 0 ? s.F0 : f == 1 ? s.F1 : s.F2;
            protected static void SetFork(DiningState s, int f, int holder)
            {
                if (f == 0) s.F0 = holder;
                else if (f == 1) s.F1 = holder;
                else s.F2 = holder;
            }
        }

        /// <summary>Thinking → Hungry.</summary>
        public sealed class BecomeHungryStep : PhilStep
        {
            public BecomeHungryStep(int i) : base(i) { }
            public override bool IsEnabled(DiningState s) => GetPC(s, I) == PhilPC.Thinking;
            public override DiningState Apply(DiningState s)
            {
                var n = (DiningState)s.Clone();
                SetPC(n, I, PhilPC.Hungry);
                return n;
            }
        }

        /// <summary>
        /// Hungry → HoldOne. Picks up this philosopher's "first" fork,
        /// requiring it to be free.
        /// </summary>
        public sealed class PickupFirstStep : PhilStep
        {
            public int FirstFork { get; }
            public PickupFirstStep(int i, int firstFork) : base(i) { FirstFork = firstFork; }
            public override bool IsEnabled(DiningState s)
                => GetPC(s, I) == PhilPC.Hungry && GetFork(s, FirstFork) == -1;
            public override DiningState Apply(DiningState s)
            {
                var n = (DiningState)s.Clone();
                SetPC(n, I, PhilPC.HoldOne);
                SetFork(n, FirstFork, I);
                return n;
            }
        }

        /// <summary>HoldOne → Eating. Picks up the second fork.</summary>
        public sealed class PickupSecondStep : PhilStep
        {
            public int SecondFork { get; }
            public PickupSecondStep(int i, int secondFork) : base(i) { SecondFork = secondFork; }
            public override bool IsEnabled(DiningState s)
                => GetPC(s, I) == PhilPC.HoldOne && GetFork(s, SecondFork) == -1;
            public override DiningState Apply(DiningState s)
            {
                var n = (DiningState)s.Clone();
                SetPC(n, I, PhilPC.Eating);
                SetFork(n, SecondFork, I);
                return n;
            }
        }

        /// <summary>Eating → Thinking. Releases both forks.</summary>
        public sealed class ReleaseStep : PhilStep
        {
            public int FirstFork { get; }
            public int SecondFork { get; }
            public ReleaseStep(int i, int firstFork, int secondFork) : base(i)
            { FirstFork = firstFork; SecondFork = secondFork; }
            public override bool IsEnabled(DiningState s) => GetPC(s, I) == PhilPC.Eating;
            public override DiningState Apply(DiningState s)
            {
                var n = (DiningState)s.Clone();
                SetPC(n, I, PhilPC.Thinking);
                SetFork(n, FirstFork, -1);
                SetFork(n, SecondFork, -1);
                return n;
            }
        }

        /// <summary>
        /// Self-loop that fires only in states where <em>no</em> per-
        /// philosopher step is enabled — i.e., the classical deadlock
        /// in the naive ordering. Makes such terminal states participate
        /// in cycles so cycle-based liveness checks can flag the absence
        /// of progress. Not covered by any fairness predicate.
        /// </summary>
        public sealed class DeadlockStutterStep : BaseStepFunction
        {
            public override string StepFunctionId => nameof(DeadlockStutterStep);
            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var s = (DiningState)state;
                if (HasAnyPhilStepEnabled(s)) return null;
                return new[]
                {
                    new StepResult
                    {
                        State = (DiningState)s.Clone(),
                        StepFunctions = new IStepFunction[] { this },
                    }
                };
            }

            private static bool HasAnyPhilStepEnabled(DiningState s)
            {
                // Any Thinking philosopher can become hungry.
                if (s.PC0 == PhilPC.Thinking || s.PC1 == PhilPC.Thinking || s.PC2 == PhilPC.Thinking)
                    return true;
                // Any Eating philosopher can release.
                if (s.PC0 == PhilPC.Eating || s.PC1 == PhilPC.Eating || s.PC2 == PhilPC.Eating)
                    return true;
                // Any Hungry philosopher with a free pickup-first fork can advance.
                // Any HoldOne philosopher with a free pickup-second fork can advance.
                // Both are captured by "some fork is free AND some philosopher in Hungry/HoldOne".
                bool anyFree = s.F0 == -1 || s.F1 == -1 || s.F2 == -1;
                if (!anyFree) return false;
                // If any fork is free, conservatively report enabled — the explorer
                // will re-check the actual step guards before generating an edge.
                return s.PC0 == PhilPC.Hungry || s.PC0 == PhilPC.HoldOne
                    || s.PC1 == PhilPC.Hungry || s.PC1 == PhilPC.HoldOne
                    || s.PC2 == PhilPC.Hungry || s.PC2 == PhilPC.HoldOne;
            }
        }
    }
}
