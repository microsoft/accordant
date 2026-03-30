// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This class represents the next set of states a given state can transition to
    /// as well as the set of new step functions that become available for application
    /// in each of those states.
    /// </summary>
    [Obsolete("This class is now obsolete. Please use the StateProfile class instead.", error: true)]
    public class NextStates
    {
        /// <summary>
        /// The set of next states, and step functions that become available in each
        /// of those states. The list has at least one element and can have more than one.
        /// Each element (which corresponds to a possible next step) can have zero or more
        /// step functions that become available in that state.
        /// </summary>
        public IList<(State State, IList<IStepFunction> StepFunctions)> StatesAndStepFunctions { get; set; }

        /// <summary>
        /// The set of next states. The list has at least one element and can have more than one.
        /// </summary>
        public IEnumerable<State> States => StatesAndStepFunctions.Select(t => t.State);

        public NextStates()
        {
        }

        public NextStates(IList<State> states)
        {
            StatesAndStepFunctions = states.Select(s =>
                (s, (IList<IStepFunction>)new IStepFunction[] { })).ToList();
        }

        /// <summary>
        /// This method returns the single next state but only if the set of next
        /// states contains a single state. It throws the <see cref="MultipleStateException"/>
        /// exception otherwise.
        /// </summary>
        public State SingleState()
        {
            if (StatesAndStepFunctions.Count == 0)
            {
                throw new UnexpectedException();
            }

            if (StatesAndStepFunctions.Count != 1)
            {
                throw new MultipleStateException();
            }

            return StatesAndStepFunctions.Single().State;
        }
    }
}
