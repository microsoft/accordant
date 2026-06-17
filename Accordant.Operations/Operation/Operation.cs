// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// The unified base class for operations.
/// 
/// An Operation defines:
/// - The spec/behavior via <see cref="Apply"/> (required)
/// - Execution logic via <see cref="ExecuteAsync"/> (optional)
/// - Request derivations via <see cref="DerivedFrom"/> (optional)
/// - Polling setup via <see cref="Polling"/> (optional)
/// </summary>
/// <typeparam name="TRequest">The type of request this operation accepts.</typeparam>
/// <typeparam name="TResponse">The type of response this operation returns.</typeparam>
/// <typeparam name="TState">The type of state this operation operates on.</typeparam>
public abstract class Operation<TRequest, TResponse, TState> :
    IOperation
    where TState : class, IState
{
    /// <summary>
    /// Static empty list for operations without derivations - avoids allocation.
    /// </summary>
    private static readonly IReadOnlyList<RequestDerivation> EmptyDerivations =
        Array.Empty<RequestDerivation>();

    /// <summary>
    /// The name of the operation. This is used to register and look up the operation in a <see cref="Spec{TState}"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The <see cref="Type"/> of the request sent to this operation.
    /// </summary>
    public Type RequestType => typeof(TRequest);

    /// <summary>
    /// The <see cref="Type"/> of the response received from this operation.
    /// </summary>
    public Type ResponseType => typeof(TResponse);

    /// <summary>
    /// Reference to the <see cref="Spec{TState}"/> this operation is registered with.
    /// This is set automatically when the operation is added to a Spec.
    /// </summary>
    public Spec<TState> Spec { get; internal set; }

    /// <summary>
    /// An optional function to execute the operation. This can be set as an alternative
    /// to overriding <see cref="ExecuteAsync(TestingContext, TRequest)"/>.
    /// </summary>
    public Func<TestingContext, TRequest, Task<TResponse>> ExecuteFunc { get; set; }

    /// <summary>
    /// The request derivations for this operation.
    /// Override this property to declare how this operation's requests can be derived
    /// from other operations' request/response pairs.
    /// Returns an empty list by default (no allocation).
    /// </summary>
    public virtual IReadOnlyList<RequestDerivation> DerivedFrom => EmptyDerivations;

    /// <summary>
    /// The polling setup for this operation.
    /// Override this property if the operation triggers background async work via
    /// a <see cref="TerminatingStepFunction"/> that needs polling.
    /// The polling request is created using a derivation defined on the polling operation.
    /// Returns null by default (no polling).
    /// </summary>
    public virtual PollingSetup Polling => null;

    /// <summary>
    /// Provides type-inferred expect methods for use within the <see cref="Apply"/> method.
    /// Using this property allows writing <c>Expect.That(r => ...)</c> and <c>.ThenState(s => ...)</c>
    /// without specifying the generic type parameters, since they are inferred from the operation's
    /// TResponse and TState types.
    /// </summary>
    /// <example>
    /// public override ExpectedOutcomes Apply(int value, StackState state)
    /// {
    ///     // No need for Expect.That&lt;int&gt; or .ThenState&lt;StackState&gt;
    ///     return Expect.That(r =&gt; r == value, "should equal value")
    ///                  .ThenState(nextState =&gt; nextState.Items.Add(value));
    /// }
    /// </example>
    protected ExpectContext<TResponse, TState> Expect { get; } = new ExpectContext<TResponse, TState>();

    /// <summary>
    /// Constructs an instance of this operation with the given name.
    /// </summary>
    /// <param name="name">The name of the operation.</param>
    protected Operation(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    #region Core Behavior (must override)

    /// <summary>
    /// Defines the behavior of this operation by returning the expected outcomes
    /// given a request and current state.
    /// 
    /// This is the core specification method that must be implemented.
    /// </summary>
    /// <param name="request">The request to process.</param>
    /// <param name="state">The current state.</param>
    /// <returns>The expected outcomes including response descriptor and next state(s).</returns>
    public abstract ExpectedOutcomes Apply(TRequest request, TState state);

    #endregion

    #region Execution (optional)

    /// <summary>
    /// Executes this operation synchronously against the system-under-test.
    /// 
    /// Override this method for simple synchronous operations. For async operations,
    /// override <see cref="ExecuteAsync(TestingContext, TRequest)"/> instead.
    /// </summary>
    /// <param name="context">The testing context.</param>
    /// <param name="request">The request to execute.</param>
    /// <returns>The response from the system.</returns>
    /// <exception cref="NotImplementedException">
    /// Thrown if neither Execute nor ExecuteAsync is overridden.
    /// </exception>
    public virtual TResponse Execute(TestingContext context, TRequest request)
    {
        throw new NotImplementedException(
            $"Execute not implemented for operation '{Name}'. " +
            $"Override Execute (sync) or ExecuteAsync (async).");
    }

    /// <summary>
    /// Executes this operation asynchronously against the system-under-test.
    /// 
    /// Override this method for async operations, or override <see cref="Execute(TestingContext, TRequest)"/>
    /// for simple synchronous operations. You can also set <see cref="ExecuteFunc"/> as an alternative.
    /// 
    /// If only <see cref="Execute(TestingContext, TRequest)"/> is overridden, this method
    /// automatically calls it and wraps the result in a completed task.
    /// </summary>
    /// <param name="context">The testing context.</param>
    /// <param name="request">The request to execute.</param>
    /// <returns>The response from the system.</returns>
    public virtual Task<TResponse> ExecuteAsync(TestingContext context, TRequest request)
    {
        if (ExecuteFunc != null)
        {
            return ExecuteFunc(context, request);
        }

        // Fall through to sync Execute - if not overridden, it throws NotImplementedException
        return Task.FromResult(Execute(context, request));
    }

    /// <summary>
    /// Non-generic execution method for <see cref="IOperation"/>.
    /// </summary>
    async Task<object> IOperation.ExecuteAsync(TestingContext context, object request)
    {
        return await ExecuteAsync(context, (TRequest)request);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates an <see cref="OperationInput"/> with the given request bound to this operation.
    /// </summary>
    /// <param name="request">The request to bind.</param>
    /// <param name="label">An optional label for the input. Defaults to the operation name.</param>
    /// <returns>An <see cref="OperationInput"/> instance.</returns>
    public OperationInput With(TRequest request, string label = null)
    {
        return new OperationInput(label ?? Name, this, request);
    }

    /// <summary>
    /// Creates an <see cref="OperationInput"/> for a parameterless operation (where TRequest is Unit).
    /// </summary>
    /// <param name="label">An optional label for the input. Defaults to the operation name.</param>
    /// <returns>An <see cref="OperationInput"/> instance.</returns>
    /// <example>
    /// // Instead of: Spec.Pop.With(Unit.Value, "Pop")
    /// // Use:        Spec.Pop.With("Pop")
    /// </example>
    public OperationInput With(string label)
    {
        return new OperationInput(label ?? Name, this, Unit.Value);
    }

    /// <summary>
    /// Creates an <see cref="OperationInput"/> with the given request bound to this operation (non-generic version).
    /// </summary>
    OperationInput IOperation.With(object request, string label)
    {
        return With((TRequest)request, label);
    }

    #endregion

    #region IOperation.Invoke Implementation

    /// <summary>
    /// Returns all possible next states and optional step functions given a request and state.
    /// </summary>
    public IList<(TResponse, StateProfile)> Invoke(TRequest request, IState state)
    {
        var expectedResults = Apply(request, (TState)state);

        var resultsAndStateProfiles = new List<(TResponse, StateProfile)>();

        foreach (var possibleResult in expectedResults.PossibleOutcomes)
        {
            var mockResponse = possibleResult.MockResponseGenerator == null ?
                default :
                (TResponse)possibleResult.MockResponseGenerator();

            var updatedStates = possibleResult.NextStateGenerator(mockResponse, state);
            var stepFunctions = possibleResult.NextStepFunctions(mockResponse)
                .Where(sf => sf != null)
                .ToList();

            var stateProfile = new StateProfile(updatedStates.Select(updatedState =>
                (updatedState,
                 (IList<IStepFunction>)stepFunctions)).ToList());

            resultsAndStateProfiles.Add((mockResponse, stateProfile));
        }

        return resultsAndStateProfiles;
    }

    /// <inheritdoc/>
    IList<(object, StateProfile)> IOperation.Invoke(object request, IState state)
    {
        return Invoke((TRequest)request, state)
            .Select(t => ((object)t.Item1, t.Item2))
            .ToList();
    }

    #endregion

    #region IOperation.Verify/ExplainInvalidResponse Implementation

    /// <inheritdoc/>
    public (bool, StateProfile) Verify(
        TRequest request,
        IState state,
        object observedResponse)
    {
        var expectedResults = Apply(request, (TState)state);

        var (isValid, stateProfile) = expectedResults.Matches(observedResponse, state);

        if (!isValid)
        {
            return (false, null);
        }

        // Replace null states (from SameState()) with the input state
        var resolvedStatesAndStepFunctions = stateProfile.StatesAndStepFunctions
            .Select(t => (t.Item1 ?? state, t.Item2))
            .ToList();

        return (true, new StateProfile(resolvedStatesAndStepFunctions));
    }

    /// <inheritdoc/>
    public (bool, StateProfile) Verify(
        TRequest request,
        StateProfile stateProfile,
        object observedResponse)
    {
        try
        {
            stateProfile = SystemChecker.Validate(
                new IStepFunction[][]
                {
                    new IStepFunction[]
                    {
                        new ContractStepFunction(
                            request,
                            observedResponse,
                            (req, state, resp) => Verify((TRequest)req, state, resp))
                    }
                },
                stateProfile);

            return (true, stateProfile);
        }
        catch (InvalidSpecException ex) when (ex.InnerException is StepFunctionApplicationException)
        {
            // The spec itself threw an exception - this is a bug in the spec, not an invalid response.
            throw;
        }
        catch (InvalidSpecException)
        {
            return (false, null);
        }
    }

    /// <inheritdoc/>
    (bool, StateProfile) IOperation.Verify(object request, IState state, object observedResponse)
    {
        return Verify((TRequest)request, state, observedResponse);
    }

    /// <inheritdoc/>
    (bool, StateProfile) IOperation.Verify(object request, StateProfile stateProfile, object observedResponse)
    {
        return Verify((TRequest)request, stateProfile, observedResponse);
    }

    /// <inheritdoc/>
    public string ExplainInvalidResponse(
        TRequest request,
        IState state,
        object observedResponse)
    {
        var tState = (TState)state;

        var expectedResults = Apply(request, tState);

        var invalidResponseExplanations = new List<string>();

        foreach (var possibleResult in expectedResults.PossibleOutcomes)
        {
            if (!possibleResult.Satisfies(observedResponse))
            {
                invalidResponseExplanations.Add(possibleResult.Explain(observedResponse));
            }
        }

        if (invalidResponseExplanations.Count == 0)
        {
            return "Response is actually valid so no explanation given.";
        }
        else if (invalidResponseExplanations.Count == 1)
        {
            return invalidResponseExplanations[0];
        }
        else
        {
            return $"The behavior predicted multiple possible responses; all of them failed to match the observed response: \r\n" +
                string.Join("\r\n\r\n", invalidResponseExplanations);
        }
    }

    /// <inheritdoc/>
    public string ExplainInvalidResponse(
        TRequest request,
        StateProfile stateProfile,
        object observedResponse)
    {
        Invariant.Assert(stateProfile != null && stateProfile.StatesAndStepFunctions.Count > 0);

        if (stateProfile.IsSingleState())
        {
            return ExplainInvalidResponse(
                request,
                stateProfile.StatesAndStepFunctions[0].State,
                observedResponse);
        }

        var sb = new StringBuilder();

        sb.AppendLine("The system can be in more than one possible state; none of the states the system " +
            "could theoretically be in explained the observed response. Here are the details for all the states:");

        try
        {
            stateProfile = SystemChecker.Validate(
                new IStepFunction[][]
                {
                    new IStepFunction[]
                    {
                        new ContractStepFunction(
                            request,
                            observedResponse,
                            (req, state, resp) => Verify((TRequest)req, state, resp))
                    }
                },
                stateProfile,
                hook: (updatedState, stepFunctions) =>
                {
                    sb.AppendLine($"Considering state: {updatedState}");
                    sb.AppendLine("---");
                    sb.AppendLine(ExplainInvalidResponse(
                        request,
                        updatedState,
                        observedResponse));
                });
        }
        catch (InvalidSpecException)
        {
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    string IOperation.ExplainInvalidResponse(object request, IState state, object observedResponse)
    {
        return ExplainInvalidResponse((TRequest)request, state, observedResponse);
    }

    /// <inheritdoc/>
    string IOperation.ExplainInvalidResponse(object request, StateProfile stateProfile, object observedResponse)
    {
        return ExplainInvalidResponse((TRequest)request, stateProfile, observedResponse);
    }

    #endregion
}
