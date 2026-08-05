namespace Accordant.Samples.Paxos
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Bounded single-decree Paxos model program.
    ///
    /// <para>
    /// <b>Bounds.</b> <see cref="P"/>=2 proposers, <see cref="A"/>=2
    /// acceptors with quorum <see cref="Quorum"/>=2. Proposer <c>p</c>
    /// always uses ballot <c>p + 1</c> (so proposer 0 → ballot 1,
    /// proposer 1 → ballot 2) and prefers value <c>p + 1</c> (proposer
    /// 0 → value 1, proposer 1 → value 2). These choices are baked into
    /// the steps to minimise state-space size while still exposing the
    /// classical Paxos interleavings.
    /// </para>
    ///
    /// <para>
    /// <b>Messages.</b> Modelled as per-pair delivery steps. A
    /// <see cref="PrepareDeliverStep"/> represents one acceptor
    /// processing one proposer's Prepare; an
    /// <see cref="AcceptDeliverStep"/> the same for Accept. Quorum
    /// detection is performed atomically by
    /// <see cref="Phase1DoneStep"/> / <see cref="Phase2DoneStep"/>,
    /// which also fix the proposer's value per the Paxos rule
    /// (carry the highest previously-accepted value seen across
    /// the quorum, else the proposer's preference).
    /// </para>
    /// </summary>
    public static class Paxos
    {
        /// <summary>
        /// Per-proposer phase in single-decree Paxos.
        /// <code>
        ///   Idle ── Phase1 attempts ─→ Promised ── Phase2 attempts ─→ Decided
        /// </code>
        /// </summary>
        public enum ProposerPhase
        {
            Idle,
            Promised,
            Decided,
        }

        /// <summary>Op tag for the last step, used by transition-style atoms.</summary>
        public enum PaxosOp
        {
            None,
            PrepareDeliver,
            Phase1Done,
            AcceptDeliver,
            Phase2Done,
        }

        public const int P = 2;
        public const int A = 2;
        public const int Quorum = 2;

        /// <summary>Ballot for proposer <paramref name="p"/> (fixed).</summary>
        public static int Ballot(int p) => p + 1;
        /// <summary>Preferred value for proposer <paramref name="p"/> (fixed, non-zero).</summary>
        public static int Preferred(int p) => p + 1;

        public static PaxosState InitialState() => new PaxosState
        {
            Phase = new ProposerPhase[P],
            ProposedValue = new int[P],
            PreparedMask = new int[P],
            AcceptedMask = new int[P],
            Promised = new int[A],
            AcceptedBallot = new int[A],
            AcceptedValue = new int[A],
            LastOp = PaxosOp.None,
            LastProposer = -1,
            LastAcceptor = -1,
        };

        public static IList<IStepFunction> AllSteps() => BuildSteps(buggyQuorum: false);

        /// <summary>
        /// Buggy variant: quorum check accepts a single response instead
        /// of a majority, breaking the Paxos safety guarantee. Used by
        /// the bug-demo to show that the Agreement property fails with a
        /// non-trivial counterexample.
        /// </summary>
        public static IList<IStepFunction> AllStepsBuggyQuorum() => BuildSteps(buggyQuorum: true);

        private static IList<IStepFunction> BuildSteps(bool buggyQuorum)
        {
            var steps = new List<IStepFunction>();
            for (int p = 0; p < P; p++)
            {
                for (int a = 0; a < A; a++)
                {
                    steps.Add(new PrepareDeliverStep(p, a));
                    steps.Add(new AcceptDeliverStep(p, a));
                }
                steps.Add(new Phase1DoneStep(p, buggyQuorum));
                steps.Add(new Phase2DoneStep(p, buggyQuorum));
            }
            steps.Add(new IdleStep());
            return steps;
        }

        // --- Atomic predicates --------------------------------------------

        public static Func<IState, bool> ProposerIs(int p, ProposerPhase ph)
            => s => ((PaxosState)s).Phase[p] == ph;

        public static Func<IState, bool> Decided(int p)
            => s => ((PaxosState)s).Phase[p] == ProposerPhase.Decided;

        public static Func<IState, bool> DecidedValue(int p, int v)
            => s =>
            {
                var st = (PaxosState)s;
                return st.Phase[p] == ProposerPhase.Decided && st.ProposedValue[p] == v;
            };

        /// <summary>Some proposer has decided.</summary>
        public static bool AnyDecided(IState s)
        {
            var st = (PaxosState)s;
            for (int p = 0; p < P; p++)
                if (st.Phase[p] == ProposerPhase.Decided) return true;
            return false;
        }

        /// <summary>
        /// Agreement predicate: every pair of decided proposers has the
        /// same value. The Paxos safety invariant.
        /// </summary>
        public static bool Agreement(IState s)
        {
            var st = (PaxosState)s;
            int v = 0;
            for (int p = 0; p < P; p++)
            {
                if (st.Phase[p] != ProposerPhase.Decided) continue;
                if (v == 0) v = st.ProposedValue[p];
                else if (st.ProposedValue[p] != v) return false;
            }
            return true;
        }

        /// <summary>
        /// Validity: the value decided was proposed by some proposer
        /// (one of <c>Preferred(p)</c>).
        /// </summary>
        public static bool Validity(IState s)
        {
            var st = (PaxosState)s;
            for (int p = 0; p < P; p++)
            {
                if (st.Phase[p] != ProposerPhase.Decided) continue;
                var v = st.ProposedValue[p];
                bool ok = false;
                for (int q = 0; q < P; q++)
                    if (Preferred(q) == v) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        // --- Steps --------------------------------------------------------

        public abstract class PaxosStep : BaseStepFunction
        {
            public abstract bool IsEnabled(PaxosState s);
            public abstract PaxosState ApplyMutate(PaxosState s);

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var s = (PaxosState)state;
                if (!IsEnabled(s)) return null;
                var n = ApplyMutate(s);
                return new[] { new StepResult { State = n, StepFunctions = new IStepFunction[] { this } } };
            }
        }

        /// <summary>
        /// Acceptor <c>a</c> processes proposer <c>p</c>'s Prepare(b_p).
        /// If <c>b_p &gt; a.Promised</c> the acceptor promises and the
        /// proposer's <see cref="PaxosState.PreparedMask"/> bit for
        /// <c>a</c> is set; otherwise the message is silently dropped
        /// (no NACK in this model).
        /// </summary>
        public sealed class PrepareDeliverStep : PaxosStep
        {
            public int Pr { get; }
            public int Ac { get; }
            public PrepareDeliverStep(int pr, int ac) { Pr = pr; Ac = ac; }
            public override string StepFunctionId => $"Prepare_{Pr}_{Ac}";

            public override bool IsEnabled(PaxosState s)
            {
                if (s.Phase[Pr] != ProposerPhase.Idle) return false;
                if ((s.PreparedMask[Pr] & (1 << Ac)) != 0) return false;
                return Ballot(Pr) > s.Promised[Ac];
            }

            public override PaxosState ApplyMutate(PaxosState s)
            {
                var n = (PaxosState)s.Clone();
                n.Promised[Ac] = Ballot(Pr);
                n.PreparedMask[Pr] |= (1 << Ac);
                n.LastOp = PaxosOp.PrepareDeliver;
                n.LastProposer = Pr;
                n.LastAcceptor = Ac;
                return n;
            }
        }

        /// <summary>
        /// Proposer <c>p</c> recognises a quorum of promises and enters
        /// <see cref="ProposerPhase.Promised"/>. By the Paxos rule, if
        /// any acceptor in the quorum reports an earlier accepted value,
        /// the proposer adopts that value (highest-ballot wins); else it
        /// uses its preferred value. In the buggy variant, the quorum
        /// threshold is 1 instead of <see cref="Quorum"/>.
        /// </summary>
        public sealed class Phase1DoneStep : PaxosStep
        {
            public int Pr { get; }
            public bool Buggy { get; }
            public Phase1DoneStep(int pr, bool buggy) { Pr = pr; Buggy = buggy; }
            public override string StepFunctionId => $"Phase1Done_{Pr}";

            public override bool IsEnabled(PaxosState s)
            {
                if (s.Phase[Pr] != ProposerPhase.Idle) return false;
                int count = System.Numerics.BitOperations.PopCount((uint)s.PreparedMask[Pr]);
                return count >= (Buggy ? 1 : Quorum);
            }

            public override PaxosState ApplyMutate(PaxosState s)
            {
                var n = (PaxosState)s.Clone();
                int bestBallot = 0, bestValue = 0;
                for (int a = 0; a < A; a++)
                {
                    if ((n.PreparedMask[Pr] & (1 << a)) == 0) continue;
                    if (n.AcceptedBallot[a] > bestBallot)
                    {
                        bestBallot = n.AcceptedBallot[a];
                        bestValue = n.AcceptedValue[a];
                    }
                }
                n.ProposedValue[Pr] = bestBallot > 0 ? bestValue : Preferred(Pr);
                n.Phase[Pr] = ProposerPhase.Promised;
                n.LastOp = PaxosOp.Phase1Done;
                n.LastProposer = Pr;
                n.LastAcceptor = -1;
                return n;
            }
        }

        /// <summary>
        /// Acceptor <c>a</c> processes proposer <c>p</c>'s Accept(b_p, v_p).
        /// If <c>b_p ≥ a.Promised</c> the acceptor records the accepted
        /// proposal and the proposer's <see cref="PaxosState.AcceptedMask"/>
        /// bit for <c>a</c> is set; else the message is dropped.
        /// </summary>
        public sealed class AcceptDeliverStep : PaxosStep
        {
            public int Pr { get; }
            public int Ac { get; }
            public AcceptDeliverStep(int pr, int ac) { Pr = pr; Ac = ac; }
            public override string StepFunctionId => $"Accept_{Pr}_{Ac}";

            public override bool IsEnabled(PaxosState s)
            {
                if (s.Phase[Pr] != ProposerPhase.Promised) return false;
                if ((s.AcceptedMask[Pr] & (1 << Ac)) != 0) return false;
                return Ballot(Pr) >= s.Promised[Ac];
            }

            public override PaxosState ApplyMutate(PaxosState s)
            {
                var n = (PaxosState)s.Clone();
                n.AcceptedBallot[Ac] = Ballot(Pr);
                n.AcceptedValue[Ac] = n.ProposedValue[Pr];
                n.AcceptedMask[Pr] |= (1 << Ac);
                n.LastOp = PaxosOp.AcceptDeliver;
                n.LastProposer = Pr;
                n.LastAcceptor = Ac;
                return n;
            }
        }

        /// <summary>
        /// Proposer <c>p</c> recognises a quorum of accepts and enters
        /// <see cref="ProposerPhase.Decided"/>.
        /// </summary>
        public sealed class Phase2DoneStep : PaxosStep
        {
            public int Pr { get; }
            public bool Buggy { get; }
            public Phase2DoneStep(int pr, bool buggy) { Pr = pr; Buggy = buggy; }
            public override string StepFunctionId => $"Phase2Done_{Pr}";

            public override bool IsEnabled(PaxosState s)
            {
                if (s.Phase[Pr] != ProposerPhase.Promised) return false;
                int count = System.Numerics.BitOperations.PopCount((uint)s.AcceptedMask[Pr]);
                return count >= (Buggy ? 1 : Quorum);
            }

            public override PaxosState ApplyMutate(PaxosState s)
            {
                var n = (PaxosState)s.Clone();
                n.Phase[Pr] = ProposerPhase.Decided;
                n.LastOp = PaxosOp.Phase2Done;
                n.LastProposer = Pr;
                n.LastAcceptor = -1;
                return n;
            }
        }

        /// <summary>Self-loop to keep terminal states non-sink and clear last-op.</summary>
        public sealed class IdleStep : BaseStepFunction
        {
            public override string StepFunctionId => "Idle";

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var n = (PaxosState)((PaxosState)state).Clone();
                n.LastOp = PaxosOp.None;
                n.LastProposer = -1;
                n.LastAcceptor = -1;
                return new[] { new StepResult { State = n, StepFunctions = new IStepFunction[] { this } } };
            }
        }
    }
}

