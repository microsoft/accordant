namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using Microsoft.Accordant;

    /// <summary>
    /// The concrete "letter" presented to a proposition during model checking.
    ///
    /// <para>Historically a proposition was evaluated against a single
    /// <see cref="State"/> (the source state of a transition). To support
    /// propositions over transitions — <c>p(s, a, s')</c> — the evaluation
    /// context now carries the full transition triple: the source state
    /// (<see cref="From"/>), the action that produced the transition
    /// (<see cref="Action"/>) together with its edge <see cref="Metadata"/>,
    /// and the target state (<see cref="To"/>).</para>
    ///
    /// <para>State-only propositions <c>p(s)</c> simply read <see cref="From"/>
    /// and ignore the rest, so they behave identically to before. A stutter
    /// self-loop (used at terminal nodes) is represented by
    /// <see cref="Stutter"/>, where <see cref="From"/> == <see cref="To"/> and
    /// <see cref="Action"/> is the reserved <see cref="StutterAction"/>.</para>
    /// </summary>
    public readonly struct TransitionContext
    {
        /// <summary>The source state <c>s</c> of the transition.</summary>
        public IState From { get; }

        /// <summary>
        /// The action <c>a</c> that produced the transition. This is the
        /// system <see cref="IStepFunction"/> annotated on the edge, or the
        /// reserved <see cref="StutterAction"/> singleton for stutter
        /// self-loops. May be <c>null</c> in the source-anchored fast path
        /// used when no proposition inspects the action (see
        /// <see cref="Source"/>).
        /// </summary>
        public IStepFunction Action { get; }

        /// <summary>The edge metadata associated with <see cref="Action"/>, if any.</summary>
        public object Metadata { get; }

        /// <summary>The target state <c>s'</c> of the transition.</summary>
        public IState To { get; }

        public TransitionContext(IState from, IStepFunction action, object metadata, IState to)
        {
            From = from;
            Action = action;
            Metadata = metadata;
            To = to;
        }

        /// <summary>
        /// A full transition letter for a concrete edge
        /// <c>from --(action)--&gt; to</c>.
        /// </summary>
        public static TransitionContext Edge(IState from, IStepFunction action, object metadata, IState to)
            => new TransitionContext(from, action, metadata, to);

        /// <summary>
        /// A stutter self-loop letter at <paramref name="state"/>:
        /// <c>state --(stutter)--&gt; state</c>. Used at terminal nodes.
        /// </summary>
        public static TransitionContext Stutter(IState state)
            => new TransitionContext(state, StutterAction.Instance, null, state);

        /// <summary>
        /// A source-anchored letter used by the fast path when no proposition
        /// inspects the action or target. <see cref="From"/> and
        /// <see cref="To"/> are both <paramref name="state"/> and
        /// <see cref="Action"/> is <c>null</c>; state-only propositions read
        /// only <see cref="From"/>, so this is behaviourally identical to the
        /// historical single-state evaluation while avoiding fabricating an
        /// action or target.
        /// </summary>
        public static TransitionContext Source(IState state)
            => new TransitionContext(state, null, null, state);
    }
}
