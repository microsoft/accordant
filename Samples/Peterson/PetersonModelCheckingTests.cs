namespace Peterson
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// Model checking of Peterson's two-process mutual-exclusion algorithm
    /// using the Accordant RLTL model-checking infrastructure.
    /// </summary>
    public class PetersonModelCheckingTests
    {
        private StateGraphNode _root;
        private FormulaBuilder<PetersonState> _p;
        private StutterSensitiveFormulaBuilder<PetersonState> _sensitive;

        // Temporal observations
        private Observation _crit0;
        private Observation _crit1;
        private Observation _want0;
        private Observation _want1;

        [SetUp]
        public void Setup()
        {
            _root = StateGraph.ExploreStateGraph(Peterson.AllSteps(), Peterson.InitialState(), lazy: true);

            _p = Formula.For<PetersonState>();
            _sensitive = _p.AllowStutterSensitiveFormulas();
            _crit0 = _p.Observe(s => s.PC0 == PetersonPC.CS, "Crit0");
            _crit1 = _p.Observe(s => s.PC1 == PetersonPC.CS, "Crit1");
            _want0 = _p.Observe(s => s.PC0 == PetersonPC.SetFlag
                                  || s.PC0 == PetersonPC.SetTurn
                                  || s.PC0 == PetersonPC.Wait, "Want0");
            _want1 = _p.Observe(s => s.PC1 == PetersonPC.SetFlag
                                  || s.PC1 == PetersonPC.SetTurn
                                  || s.PC1 == PetersonPC.Wait, "Want1");
        }

        // --- Safety -------------------------------------------------------

        /// <summary>
        /// Mutual exclusion: □¬(Crit0 ∧ Crit1).
        /// </summary>
        [Test]
        public void Safety_MutualExclusion()
        {
            var mutex = _p.Always(!(_crit0 & _crit1));
            var result = _root.Check(mutex);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// With a buggy model, mutual exclusion fails.
        /// </summary>
        [Test]
        public void Safety_MutualExclusion_FailsWithBug()
        {
            var buggyRoot = StateGraph.ExploreStateGraph(
                Peterson.AllStepsBuggy(), Peterson.InitialState(), lazy: true);
            var mutex = _p.Always(!(_crit0 & _crit1));
            var result = buggyRoot.Check(mutex);
            Assert.IsFalse(result.Valid,
                "Buggy model should violate mutual exclusion.");
        }

        // --- Liveness under weak fairness ---------------------------------

        /// <summary>
        /// Starvation freedom for process 0: □(Want0 → ◇Crit0).
        /// </summary>
        [Test]
        public void Liveness_StarvationFreedom_Process0()
        {
            var phi = _p.LeadsTo(_want0, _crit0);
            var result = _root.Check(phi, fairness: Fairness.WeakAll);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// Starvation freedom for process 1: □(Want1 → ◇Crit1).
        /// </summary>
        [Test]
        public void Liveness_StarvationFreedom_Process1()
        {
            var phi = _p.LeadsTo(_want1, _crit1);
            var result = _root.Check(phi, fairness: Fairness.WeakAll);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// Without fairness, starvation freedom fails.
        /// </summary>
        [Test]
        public void Liveness_StarvationFreedom_FailsWithoutFairness()
        {
            var phi = _p.LeadsTo(_want0, _crit0);
            var result = _root.Check(phi, fairness: Fairness.None);
            Assert.IsFalse(result.Valid,
                "Without fairness, an unfair cycle keeps process 0 starved.");
        }

        /// <summary>
        /// Infinitely often: □◇Crit0 ∧ □◇Crit1 under weak fairness.
        /// </summary>
        [Test]
        public void Liveness_BothProcessesEnterCSInfinitelyOften()
        {
            var phi = _p.InfinitelyOften(_crit0) & _p.InfinitelyOften(_crit1);
            var result = _root.Check(phi, fairness: Fairness.WeakAll);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- RLTL regex properties ----------------------------------------

        /// <summary>
        /// Bounded overtaking via regex trigger: process 1 cannot enter CS
        /// twice while process 0 is spinning without process 0 getting in.
        /// </summary>
        [Test]
        public void Regex_BoundedOvertaking()
        {
            var inWait0 = _p.Observe(s => s.PC0 == PetersonPC.Wait, "InWait0");
            var inWait1 = _p.Observe(s => s.PC1 == PetersonPC.Wait, "InWait1");

            // Forbidden pattern for process 0:
            // Σ* · (InWait0 ∧ Crit1) · (InWait0 ∧ ¬Crit1)* · (InWait0 ∧ Crit1)
            RegexPattern w0c1 = inWait0 & _crit1;
            RegexPattern w0nc1 = inWait0 & !_crit1;
            var bad0 = RegexPattern.Sigma.Star()
                .Then(w0c1)
                .Then(w0nc1.Star())
                .Then(w0c1);

            // Symmetric for process 1
            RegexPattern w1c0 = inWait1 & _crit0;
            RegexPattern w1nc0 = inWait1 & !_crit0;
            var bad1 = RegexPattern.Sigma.Star()
                .Then(w1c0)
                .Then(w1nc0.Star())
                .Then(w1c0);

            var phi = _sensitive.Trigger(bad0, _p.False)
                & _sensitive.Trigger(bad1, _p.False);
            var result = _root.Check(phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// Whenever process 0 is in CS, process 1 is not (via regex match).
        /// </summary>
        [Test]
        public void Regex_WheneverCrit0_NotCrit1()
        {
            RegexPattern prefix = RegexPattern.Sigma.Star().Then(_crit0);
            var phi = _sensitive.Match(prefix, _p.Not(_crit1));
            var result = _root.Check(phi);
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        // --- Stutter-safe regex properties (SafeRegex) --------------------

        /// <summary>
        /// A <see cref="SafeRegex"/> pattern counts the model's
        /// <em>changing</em> steps, not its physical transitions. Reaching the
        /// critical section takes four of them (Request, SetFlag, SetTurn,
        /// EnterCS), so after the first three neither process can be inside.
        /// </summary>
        [Test]
        public void SafeRegex_EnteringTheCriticalSectionTakesFourSteps()
        {
            var step = _p.AnyChangingStep;
            var phi = _p.Whenever(
                step.Then(step).Then(step),
                (!_crit0) & !_crit1);
            var result = _root.Check(phi.Named("No CS within three changing steps"));
            Assert.IsTrue(result.Valid, result.GetTraceString());
        }

        /// <summary>
        /// A forbidden pattern can be written either as
        /// <c>Whenever(R, False)</c> or as <c>!After(R, True)</c>; both stay
        /// <see cref="StutterSafeFormula"/>. Here: no changing step is ever
        /// taken from a state with both processes in the critical section.
        /// </summary>
        [Test]
        public void SafeRegex_NoStepEverStartsWithBothProcessesCritical()
        {
            var doubleCritical = _p.ChangingStep(_crit0 & _crit1);

            StutterSafeFormula viaWhenever = _p.Whenever(doubleCritical, _p.False);
            StutterSafeFormula viaAfter = !_p.After(doubleCritical, _p.True);

            Assert.IsTrue(_root.Check(viaWhenever).Valid);
            Assert.IsTrue(_root.Check(viaAfter).Valid);
        }

        /// <summary>
        /// The payoff. Adding a state-neutral <see cref="IdleStep"/> to the
        /// model — an action that is offered but changes nothing — leaves a
        /// stutter-safe step-counting property intact, while the equivalent
        /// pattern over <em>physical</em> letters breaks: two Idle letters
        /// count as two steps although nothing happened.
        /// </summary>
        [Test]
        public void SafeRegex_StepCounting_SurvivesNoOpModelEdges()
        {
            var steps = new List<IStepFunction>(Peterson.AllSteps()) { new IdleStep() };
            var idlingRoot = StateGraph.ExploreStateGraph(
                steps, Peterson.InitialState(), lazy: true);

            var bothIdle = _p.Observe(
                s => s.PC0 == PetersonPC.NCS && s.PC1 == PetersonPC.NCS, "BothInNCS");

            // Two changing steps always take at least one process out of NCS.
            var safe = _p.Whenever(
                _p.AnyChangingStep.Then(_p.AnyChangingStep), !bothIdle);
            Assert.IsTrue(_root.Check(safe).Valid, "safe property on the plain model");
            Assert.IsTrue(
                idlingRoot.Check(safe).Valid,
                "the Idle edges are invisible to a changing-step pattern");

            // The same shape over physical letters counts the Idle edges.
            RegexPattern anyLetter = _p.Observe(s => true, "AnyLetter");
            var sensitive = _sensitive.Trigger(
                anyLetter.Then(anyLetter), _p.Not(bothIdle));
            Assert.IsTrue(
                _root.Check(sensitive).Valid,
                "sensitive property on the plain model");
            Assert.IsFalse(
                idlingRoot.Check(sensitive).Valid,
                "two Idle letters leave both processes in NCS");
        }

        // --- Shared definitions -------------------------------------------

        /// <summary>
        /// A named model edge that leaves the state unchanged. Real models
        /// acquire these from idle ticks, no-op retries, or re-reading a value
        /// that has not moved.
        /// </summary>
        private sealed class IdleStep : BaseStepFunction
        {
            public override string StepFunctionId => "Idle";

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
    }
}
