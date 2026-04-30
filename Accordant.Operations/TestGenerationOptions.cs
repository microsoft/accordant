// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Context provided to the ShouldUnwindStepFunction lambda during test generation.
    /// </summary>
    public class UnwindContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnwindContext"/> class.
        /// </summary>
        /// <param name="operation">The operation that triggered the step function.</param>
        /// <param name="request">The request that was sent to the operation.</param>
        /// <param name="state">The state in which the step function was triggered.</param>
        /// <param name="stepFunction">The step function to potentially unwind.</param>
        public UnwindContext(IOperation operation, object request, IState state, IStepFunction stepFunction)
        {
            Operation = operation;
            Request = request;
            State = state;
            StepFunction = stepFunction;
        }

        /// <summary>
        /// The operation that triggered the step function.
        /// </summary>
        public IOperation Operation { get; }

        /// <summary>
        /// The request that was sent to the operation.
        /// </summary>
        public object Request { get; }

        /// <summary>
        /// The state in which the step function was triggered.
        /// </summary>
        public IState State { get; }

        /// <summary>
        /// The step function to potentially unwind.
        /// </summary>
        public IStepFunction StepFunction { get; }
    }

    /// <summary>
    /// This class contains options and configurations used during
    /// the test case generation process.
    /// </summary>
    public class TestGenerationOptions
    {
        /// <summary>
        /// This lambda indicates whether the given operation input
        /// should be applied in a given state or not.
        /// </summary>
        public Func<OperationInput, IState, bool> ShouldApply { get; set; } = (_, _) => true;

        /// <summary>
        /// This lambda controls whether exploration should stop at the given
        /// state or whether next states should be generated for further exploration.
        /// This lambda is only checked after the <see cref="MaxDepth"/> check passes.
        /// </summary>
        public Func<IState, bool> StateConstraint { get; set; }

        /// <summary>
        /// This property controls the max depth of the state space graph
        /// to explore up to when generating test cases. This property
        /// is always checked before the <see cref="StateConstraint"/>
        /// property.
        /// 
        /// Default is 5 to prevent unbounded exploration. Set to -1 for unlimited
        /// depth (use with caution - can cause infinite loops with cyclic state graphs).
        /// </summary>
        public int MaxDepth { get; set; } = 5;

        /// <summary>
        /// This property controls whether the operation call names in test cases
        /// should be simplified.
        /// </summary>
        public bool SimplifyOperationCallNames { get; set; } = true;

        /// <summary>
        /// The maximum number of concurrent operations that should be invoked together
        /// in a test. This property can be set to -1 if no bound is required on the max
        /// number of concurrent calls in a test.
        /// </summary>
        public int MaxConcurrencyLevel { get; set; } = 3;

        /// <summary>
        /// A dictionary mapping an operation name to a function that returns a template request.
        /// Templates are used when deriving requests from other operations, allowing a fuzzer
        /// or other external source to provide base request values that the derivation
        /// lambda can then modify with data from the source operation.
        /// </summary>
        public Dictionary<string, Func<object>> RequestTemplates { get; set; } =
            new Dictionary<string, Func<object>>();

        /// <summary>
        /// List of derivation selectors that filter which request derivations are generated
        /// during the test generation phase. All derivations are generated if this property
        /// is set to null. Set to an empty list to disable all derivations.
        /// </summary>
        public IList<DerivationSelector> DerivationSelectors { get; set; } = null;

        /// <summary>
        /// The algorithm to use for sequential test case generation.
        /// </summary>
        public SequentialTestCaseAlgorithm SequentialTestCaseAlgorithm { get; set; } =
            SequentialTestCaseAlgorithms.StateCoverage;

        /// <summary>
        /// The algorithm to use for concurrent test case generation.
        /// </summary>
        public ConcurrentTestCaseAlgorithm ConcurrentTestCaseAlgorithm { get; set; } =
            ConcurrentTestCaseAlgorithms.CreateDefaultConcurrentTestCaseGenerator(maxConcurrencyLevel: 3);

        /// <summary>
        /// This lambda indicates whether to include a transition where an operation call takes
        /// the system from one state to another in the state graph that is used to generate
        /// test cases.
        /// </summary>
        public Func<IState, OperationCall, IState, bool> ShouldIncludeTransition { get; set; }

        /// <summary>
        /// This indicates whether a given operation input is preserved after application or not.
        /// The lambda is given the operation input as well as the list of all operation inputs (of all types)
        /// that have been executed on this path so far (including itself, always at the end)
        /// </summary>
        public Func<OperationInput, IList<OperationInput>, bool> ShouldPreserveOperation { get; set; } = (_, _) => true;

        /// <summary>
        /// This configures the ShouldPreserveOperation lambda such that no operation
        /// is applied more than once. This property does not have a getter. If you want
        /// more flexibility, consider specifying the ShouldPreserveOperation lambda directly.
        /// </summary>
        public bool ApplyOperationsRepeatedly
        {
            set
            {
                ShouldPreserveOperation = (_, _) => value;
            }
        }

        /// <summary>
        /// This configures the ShouldPreserveOperation lambda such that no operation
        /// is applied more than configured amount of time on any path.
        /// This property does not have a getter. If you want more flexibility,
        /// consider specifying the ShouldPreserveOperation lambda directly.
        /// </summary>
        public int MaxOperationApplicationCount
        {
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException($"Value must be >= 0.");
                }

                ShouldPreserveOperation = (input, inputs) =>
                {
                    var inputCount = inputs.Count(i => i == input);
                    return inputCount < value;
                };
            }
        }

        /// <summary>
        /// This lambda is used to decide whether step functions produced by operations should be "unwound" i.e.
        /// repeatedly applied till they reach their terminal state as defined by 
        /// <see cref="TerminatingStepFunction.IsTerminalState"/>.
        ///
        /// Step functions are typically used to model async background processes. As an example,
        /// you might have model an API where the user can upload an image, initially in the `Creating`
        /// state and which triggers a background async process that creates a thumbnail for the image,
        /// ultimately changing the state of the image to `Created` (or `Failed`, if it encounters a
        /// problem). This async functionality is typically modeled using step functions.
        /// 
        /// The lambda receives an <see cref="UnwindContext"/> with the operation, request, state, and step function.
        /// Only <see cref="TerminatingStepFunction"/> instances can be unwound (they define their terminal state).
        /// 
        /// Default: All terminating step functions are unwound.
        /// </summary>
        public Func<UnwindContext, bool> ShouldUnwindStepFunction { get; set; } = 
            ctx => ctx.StepFunction is TerminatingStepFunction;

        /// <summary>
        /// Convenience property: Set to true to unwind all <see cref="TerminatingStepFunction"/> instances.
        /// This is equivalent to setting ShouldUnwindStepFunction to check if the step function
        /// is a TerminatingStepFunction.
        /// </summary>
        public bool UnwindAllTerminatingStepFunctions
        {
            set
            {
                ShouldUnwindStepFunction = ctx => value && ctx.StepFunction is TerminatingStepFunction;
            }
        }
    }
}
