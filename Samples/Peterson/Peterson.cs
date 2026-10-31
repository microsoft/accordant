namespace Peterson
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Peterson's algorithm for mutual exclusion of two processes
    /// (Peterson, 1981). The classical pseudocode for process <c>i</c> with
    /// <c>j = 1 - i</c> is:
    /// <code>
    ///   loop forever:
    ///     non-critical section
    ///     flag[i] := true
    ///     turn   := j
    ///     while flag[j] ∧ turn = j: skip
    ///     critical section
    ///     flag[i] := false
    /// </code>
    /// Each statement is realised here as a separate
    /// <see cref="IStepFunction"/> so that the model checker can interleave
    /// the two processes freely. Compound operations are kept atomic at the
    /// per-statement granularity — sufficient to expose all the
    /// interesting interleavings (the busy-wait loop is collapsed into a
    /// single guarded <see cref="EnterCSStep"/>).
    /// </summary>
    public static class Peterson
    {
        // --- Atomic predicates --------------------------------------------

        /// <summary>Process 0 is in the critical section.</summary>
        public static bool Crit0(State s) => ((PetersonState)s).PC0 == PetersonPC.CS;

        /// <summary>Process 1 is in the critical section.</summary>
        public static bool Crit1(State s) => ((PetersonState)s).PC1 == PetersonPC.CS;

        /// <summary>Process 0 is busy-waiting at the spin loop (<c>PC0 = Wait</c>).</summary>
        public static bool InWait0(State s) => ((PetersonState)s).PC0 == PetersonPC.Wait;

        /// <summary>Process 1 is busy-waiting at the spin loop (<c>PC1 = Wait</c>).</summary>
        public static bool InWait1(State s) => ((PetersonState)s).PC1 == PetersonPC.Wait;

        /// <summary>Process 0 has indicated intent and is not in CS yet.</summary>
        public static bool Want0(State s)
        {
            var pc = ((PetersonState)s).PC0;
            return pc == PetersonPC.SetFlag || pc == PetersonPC.SetTurn || pc == PetersonPC.Wait;
        }

        /// <summary>Process 1 has indicated intent and is not in CS yet.</summary>
        public static bool Want1(State s)
        {
            var pc = ((PetersonState)s).PC1;
            return pc == PetersonPC.SetFlag || pc == PetersonPC.SetTurn || pc == PetersonPC.Wait;
        }

        // --- State-graph construction ------------------------------------

        /// <summary>
        /// Returns the initial state: both processes idle in <c>NCS</c>,
        /// flags clear, <c>turn = 0</c>.
        /// </summary>
        public static PetersonState InitialState()
            => new PetersonState
            {
                Flag0 = false,
                Flag1 = false,
                Turn = 0,
                PC0 = PetersonPC.NCS,
                PC1 = PetersonPC.NCS,
            };

        /// <summary>
        /// Returns the list of step functions for the two-process variant.
        /// Each process contributes one step per pseudocode statement.
        /// </summary>
        public static IList<IStepFunction> AllSteps() => BuildSteps(buggy: false);

        /// <summary>
        /// Bug-injection variant: replaces <see cref="EnterCSStep"/> with
        /// <see cref="BuggyEnterCSStep"/>, which drops both Peterson guard
        /// clauses (<c>¬flag[j] ∨ turn = i</c>) and so admits the
        /// critical section unconditionally once the process is in
        /// <see cref="PetersonPC.Wait"/>. Mutual exclusion fails.
        /// </summary>
        public static IList<IStepFunction> AllStepsBuggy() => BuildSteps(buggy: true);

        private static IList<IStepFunction> BuildSteps(bool buggy)
        {
            var steps = new List<IStepFunction>();
            for (int i = 0; i < 2; i++)
            {
                steps.Add(new RequestStep(i));
                steps.Add(new SetFlagStep(i));
                steps.Add(new SetTurnStep(i));
                steps.Add(buggy ? (PetersonStep)new BuggyEnterCSStep(i) : new EnterCSStep(i));
                steps.Add(new ExitCSStep(i));
                steps.Add(new ResetFlagStep(i));
            }
            return steps;
        }

        // --- Step functions ----------------------------------------------

        /// <summary>Common scaffolding for a deterministic Peterson step.</summary>
        public abstract class PetersonStep : BaseStepFunction
        {
            /// <summary>Process index (0 or 1) this step belongs to.</summary>
            public int I { get; }
            /// <summary>Index of the other process — <c>1 - I</c>.</summary>
            protected int J => 1 - I;

            protected PetersonStep(int i) { I = i; }

            /// <summary>Predicate selecting the states this step is enabled in.</summary>
            public abstract bool IsEnabled(PetersonState s);
            /// <summary>Computes the unique successor state.</summary>
            public abstract PetersonState Apply(PetersonState s);

            /// <summary>
            /// Stable id so two equally-configured steps in different
            /// graph nodes hash identically (required by the fairness checker
            /// which keys on <see cref="IStepFunction.StepFunctionId"/>).
            /// </summary>
            public override string StepFunctionId => GetType().Name + "_" + I;

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var s = (PetersonState)state;
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

            /// <summary>Mutates the chosen process's PC; helper for subclasses.</summary>
            protected PetersonState WithPC(PetersonState s, PetersonPC pc)
            {
                var next = (PetersonState)s.Clone();
                if (I == 0) next.PC0 = pc; else next.PC1 = pc;
                return next;
            }
        }

        /// <summary>NCS → SetFlag — process decides to enter.</summary>
        public sealed class RequestStep : PetersonStep
        {
            public RequestStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.NCS;
            public override PetersonState Apply(PetersonState s)
                => WithPC(s, PetersonPC.SetFlag);
        }

        /// <summary>SetFlag → SetTurn — <c>flag[i] := true</c>.</summary>
        public sealed class SetFlagStep : PetersonStep
        {
            public SetFlagStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.SetFlag;
            public override PetersonState Apply(PetersonState s)
            {
                var next = WithPC(s, PetersonPC.SetTurn);
                if (I == 0) next.Flag0 = true; else next.Flag1 = true;
                return next;
            }
        }

        /// <summary>SetTurn → Wait — <c>turn := 1 - i</c>.</summary>
        public sealed class SetTurnStep : PetersonStep
        {
            public SetTurnStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.SetTurn;
            public override PetersonState Apply(PetersonState s)
            {
                var next = WithPC(s, PetersonPC.Wait);
                next.Turn = J;
                return next;
            }
        }

        /// <summary>
        /// Wait → CS — guard <c>¬flag[j] ∨ turn = i</c>. Collapses the
        /// busy-wait spin into a single enabling guard (the algorithm's
        /// semantics are insensitive to the number of guard evaluations
        /// while waiting, so a self-loop is unnecessary).
        /// </summary>
        public sealed class EnterCSStep : PetersonStep
        {
            public EnterCSStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
            {
                var myPC = I == 0 ? s.PC0 : s.PC1;
                if (myPC != PetersonPC.Wait) return false;
                var otherFlag = I == 0 ? s.Flag1 : s.Flag0;
                return !otherFlag || s.Turn == I;
            }
            public override PetersonState Apply(PetersonState s)
                => WithPC(s, PetersonPC.CS);
        }

        /// <summary>CS → Exit — leave the critical section.</summary>
        public sealed class ExitCSStep : PetersonStep
        {
            public ExitCSStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.CS;
            public override PetersonState Apply(PetersonState s)
                => WithPC(s, PetersonPC.Exit);
        }

        /// <summary>
        /// Bug-injection variant of <see cref="EnterCSStep"/>: drops the
        /// Peterson guard <c>¬flag[j] ∨ turn = i</c> entirely, admitting
        /// <see cref="PetersonPC.Wait"/> → <see cref="PetersonPC.CS"/>
        /// unconditionally. Used by <see cref="AllStepsBuggy"/> to expose
        /// a mutual-exclusion counterexample.
        /// </summary>
        public sealed class BuggyEnterCSStep : PetersonStep
        {
            public BuggyEnterCSStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.Wait;
            public override PetersonState Apply(PetersonState s)
                => WithPC(s, PetersonPC.CS);
            // Re-use the correct step's id so fairness-keyed analyses
            // treat the buggy step as the same enabling family.
            public override string StepFunctionId => "EnterCSStep_" + I;
        }

        /// <summary>Exit → NCS — <c>flag[i] := false</c>.</summary>
        public sealed class ResetFlagStep : PetersonStep
        {
            public ResetFlagStep(int i) : base(i) { }
            public override bool IsEnabled(PetersonState s)
                => (I == 0 ? s.PC0 : s.PC1) == PetersonPC.Exit;
            public override PetersonState Apply(PetersonState s)
            {
                var next = WithPC(s, PetersonPC.NCS);
                if (I == 0) next.Flag0 = false; else next.Flag1 = false;
                return next;
            }
        }
    }
}
