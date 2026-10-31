namespace TerminationDetection
{
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;

    /// <summary>
    /// Step functions in bragger specs are _consumed_ after they are applied to a given state.
    /// This is unlike the behavior in TLA+ where the set of actions in a specification is a constant
    /// and never changes. They may be enabled or disabled in a particular state (similar to 
    /// step functions in bragger specs) but the act of applying them to a state does not
    /// _consume_ them. Unlike TLA+, step functions in bragger specs can also _produce_ zero or
    /// more step functions for subsequent states. We use this property of step functions
    /// and produce the very same step function as a result of the
    /// <c>BaseStepFunction.Apply</c> operation, thus producing the
    /// same step function that was just consumed. This allows us to achieve the semantics of
    /// TLA+ actions where the set of actions remains constant throughout the model checking of
    /// a specification.
    /// </summary>
    public abstract class TLAStepFunction : BaseStepFunction
    {
        public abstract bool IsEnabled(SystemState systemState);

        public abstract IList<SystemState> NextStates(SystemState systemState);

        protected override IList<StepResult> ApplyInternal(IState state)
        {
            var systemState = (SystemState)state;

            if (!IsEnabled(systemState))
            {
                return null;
            }

            var nextStates = NextStates(systemState);
            return
                nextStates.Select(s =>
                    new StepResult()
                    {
                        State = (State)s,
                        StepFunctions = new IStepFunction[] { this }
                    }).ToList();
        }
    }
}
