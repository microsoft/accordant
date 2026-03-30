// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// An expected outcome includes a response descriptor that can be used to verify whether
    /// the observed response from the system is valid or not, an update state as well
    /// as an optional step function that runs concurrently with the rest of the system.
    /// </summary>
    public class ExpectedOutcome
    {
        /// <summary>
        /// The validator used to check whether the observed response
        /// satisfies the expected constraints or not.
        /// </summary>
        public ResponseValidator Validator { get; }

        /// <summary>
        /// A function that computes the next state(s) given the response and current state.
        /// </summary>
        public Func<object, State, StateList> NextStateGenerator { get; }

        /// <summary>
        /// A function that computes step functions given the response.
        /// Returns empty list if no step functions are triggered.
        /// </summary>
        public Func<object, StepFunctionList> NextStepFunctions { get; }

        /// <summary>
        /// A function that returns a mock response for state exploration.
        /// </summary>
        public Func<object> MockResponseGenerator { get; }

        #region Primary Constructor

        /// <summary>
        /// Primary constructor - all other constructors normalize to this form.
        /// </summary>
        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, State, StateList> nextStateGenerator,
            Func<object, StepFunctionList> nextStepFunctions,
            Func<object> mockResponseGenerator = null)
        {
            Validator = validator ?? throw new ArgumentNullException(nameof(validator));
            NextStateGenerator = nextStateGenerator ?? throw new ArgumentNullException(nameof(nextStateGenerator));
            NextStepFunctions = nextStepFunctions ?? ((resp) => new StepFunctionList());
            MockResponseGenerator = mockResponseGenerator;
        }

        #endregion

        #region Backward Compatible Constructors - Fixed State

        public ExpectedOutcome(
            ResponseValidator validator,
            State updatedState,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedState != null ? [updatedState] : [state],
                (resp) => new StepFunctionList(),
                mockResponse)
        {
        }

        public ExpectedOutcome(
            ResponseValidator validator,
            State updatedState,
            IStepFunction stepFunction,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedState != null ? [updatedState] : [state],
                (resp) => stepFunction != null ? new StepFunctionList(stepFunction) : new StepFunctionList(),
                mockResponse)
        {
        }

        public ExpectedOutcome(
            ResponseValidator validator,
            State updatedState,
            Func<object, StepFunctionList> stepFunctionGenerator,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedState != null ? [updatedState] : [state],
                stepFunctionGenerator ?? ((resp) => new StepFunctionList()),
                mockResponse)
        {
        }

        #endregion

        #region Backward Compatible Constructors - Response-Dependent State

        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, StateList> updatedStateGenerator,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedStateGenerator(resp),
                (resp) => new StepFunctionList(),
                mockResponse)
        {
        }

        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, StateList> updatedStateGenerator,
            IStepFunction stepFunction,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedStateGenerator(resp),
                (resp) => stepFunction != null ? new StepFunctionList(stepFunction) : new StepFunctionList(),
                mockResponse)
        {
        }

        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, StateList> updatedStateGenerator,
            Func<object, StepFunctionList> stepFunctionGenerator,
            Func<object> mockResponse = null)
            : this(
                validator,
                (resp, state) => updatedStateGenerator(resp),
                stepFunctionGenerator ?? ((resp) => new StepFunctionList()),
                mockResponse)
        {
        }

        #endregion

        #region Backward Compatible Constructors - State Transition (response + current state)

        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, State, StateList> stateTransition,
            Func<object> mockResponse = null)
            : this(
                validator,
                stateTransition,
                (resp) => new StepFunctionList(),
                mockResponse)
        {
        }

        public ExpectedOutcome(
            ResponseValidator validator,
            Func<object, State, StateList> stateTransition,
            IStepFunction stepFunction,
            Func<object> mockResponse = null)
            : this(
                validator,
                stateTransition,
                (resp) => stepFunction != null ? new StepFunctionList(stepFunction) : new StepFunctionList(),
                mockResponse)
        {
        }

        // Note: The (stateTransition, stepFunctionGenerator) variant is the same as the primary constructor

        #endregion

        /// <summary>
        /// This method validates whether the observed response matches
        /// the expectation. If the observed response is valid, it returns
        /// the next state(s) and step function(s) as a StateProfile.
        /// </summary>
        /// <param name="observedResponse">The response to validate.</param>
        /// <param name="currentState">The current state.</param>
        /// <returns>
        /// A tuple of (isValid, stateProfile).
        /// </returns>
        public (bool, StateProfile) Matches(object observedResponse, State currentState)
        {
            if (Validator == null)
            {
                throw new InvalidOperationException(
                    "ExpectedOutcome must have a Validator set.");
            }

            var result = Validator.Validate(observedResponse);
            if (!result.IsValid)
            {
                return (false, null);
            }

            var nextStates = NextStateGenerator(observedResponse, currentState);
            var stepFunctions = NextStepFunctions(observedResponse)
                .Where(sf => sf != null)
                .ToList();

            var statesAndStepFunctions = nextStates
                .Select(s => (s, (IList<IStepFunction>)stepFunctions))
                .ToList();

            return (true, new StateProfile(statesAndStepFunctions));
        }

        /// <summary>
        /// Returns an explanation of why the observed response did not match
        /// this expected outcome.
        /// </summary>
        public string Explain(object observedResponse)
        {
            if (Validator == null)
            {
                return "No validator set.";
            }

            return Validator.Explain(observedResponse);
        }

        /// <summary>
        /// Checks if the observed response satisfies this expected outcome
        /// without computing the next state.
        /// </summary>
        public bool Satisfies(object observedResponse)
        {
            if (Validator == null)
            {
                throw new InvalidOperationException(
                    "ExpectedOutcome must have a Validator set.");
            }

            return Validator.Validate(observedResponse).IsValid;
        }

        public static implicit operator ExpectedOutcomes(ExpectedOutcome expectedResult)
        {
            return new ExpectedOutcomes(expectedResult);
        }
    }

    /// <summary>
    /// This class contains collection of <see cref="ExpectedOutcome"/> that
    /// represent the different outcomes of invoking an operation.
    /// </summary>
    public class ExpectedOutcomes
    {
        /// <summary>
        /// List of possible outcomes of invoking an operation.
        /// </summary>
        public IList<ExpectedOutcome> PossibleOutcomes { get; private set; }

        public ExpectedOutcomes(params ExpectedOutcome[] possibleOutcomes)
        {
            if (possibleOutcomes.Length == 0)
            {
                throw new EmptyExpectedOutcomesException();
            }

            PossibleOutcomes = possibleOutcomes;
        }

        /// <summary>
        /// This method matches the observed response against the list of possible
        /// outcomes, returning the next state and step function of all the results
        /// that match.
        /// </summary>
        /// <param name="observedResponse">The response to validate.</param>
        /// <param name="currentState">The current state.</param>
        /// <returns>
        /// A tuple of (isValid, stateProfile).
        /// </returns>
        public (bool, StateProfile) Matches(object observedResponse, State currentState)
        {
            var statesAndStepFunctions = new List<(State, IList<IStepFunction>)>();

            foreach (var possibleOutcome in PossibleOutcomes)
            {
                var (valid, stateProfile) =
                    possibleOutcome.Matches(observedResponse, currentState);

                if (valid)
                {
                    statesAndStepFunctions.AddRange(stateProfile.StatesAndStepFunctions);
                }
            }

            if (statesAndStepFunctions.Count == 0)
            {
                return (false, null);
            }
            else
            {
                return (true, new StateProfile(statesAndStepFunctions));
            }
        }
    }

    /// <summary>
    /// This class represents a list of states.
    /// </summary>
    public class StateList : List<State>
    {
        public StateList(params State[] states) : base(states)
        {
        }

        public static implicit operator StateList(State state)
        {
            return new StateList(state);
        }
    }

    /// <summary>
    /// This class represents a list of step functions.
    /// </summary>
    public class StepFunctionList : List<IStepFunction>
    {
        public StepFunctionList(params IStepFunction[] stepFunctions) : base(stepFunctions)
        {
        }

        public static implicit operator StepFunctionList(BaseStepFunction stepFunction)
        {
            return new StepFunctionList(stepFunction);
        }
    }
}
