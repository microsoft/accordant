namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// The reserved "stutter" action. A stutter self-loop leaves the system
    /// state unchanged; it is emitted at terminal nodes so that every complete
    /// run of the product automaton is infinite (a requirement for
    /// ω-acceptance). Depth frontiers are not stuttered because their
    /// continuation is unknown.
    ///
    /// <para>Before propositions over transitions were supported, stutter
    /// self-loops carried a <c>null</c> step function. With
    /// <c>p(s, a, s')</c> propositions, the action must be observable, so the
    /// stutter self-loop is labelled with this singleton. A proposition can
    /// detect it via <see cref="Transition.IsStutter"/>.</para>
    ///
    /// <para>This step function is a pure label: it is never applied by the
    /// explorer, and <see cref="Apply"/> returns no successors.</para>
    /// </summary>
    public sealed class StutterAction : IStepFunction
    {
        /// <summary>The shared stutter-action singleton.</summary>
        public static readonly StutterAction Instance = new StutterAction();

        private StutterAction() { }

        /// <summary>Stable identifier for the stutter action.</summary>
        public string StepFunctionId => "__accordant_stutter__";

        /// <summary>
        /// The stutter action is a label only and is never applied; this
        /// returns no successors.
        /// </summary>
        public IList<StepResult> Apply(
            IState state,
            IReadOnlyList<(IStepFunction, StateGraphNode)> path)
            => new List<StepResult>();

        public override string ToString() => "stutter";
    }
}
