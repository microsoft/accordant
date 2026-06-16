// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// This class tracks the state or set of states the system could be in, as well
/// as active step functions that can further impact that state.
///
/// The most frequent use case is the system being in a single state, and each operation
/// optionally transitioning it to a single next state.
///
/// There are situations however where the system can be in one of a possible set of
/// states.  As an example, consider a web service endpoint that returns a timeout
/// error. A timeout error might not always mean the operation did not
/// finish. Maybe it didn't, or maybe it did but the server's timeout stopwatch
/// expired before it could return a successful response. So the system can be in one
/// of two states: one in which the operation never happened and the other in which it
/// did happen. This class can allow us to represent both the possibilities. In such
/// situations, further operations (say a GET call on the web service endpoint) can
/// help resolve the ambiguity the system is in.
/// 
/// Finally, certain operations can trigger active processes within the system that
/// can transition the system to possible next states without the user taking any
/// explicit action. As an example, consider a web service endpoint that starts a long
/// running background process that can transition the system through a sequence of 
/// states. This class can represent such processes by associating a set of step functions
/// associated with each possible state the system can be in.
/// </summary>
public class StateProfile
{
    /// <summary>
    /// The set of states the system can be and the set of step functions
    /// associated with each of those states.
    /// </summary>
    public IList<(IState State, IList<IStepFunction> StepFunctions)> StatesAndStepFunctions { get; set; }

    /// <summary>
    /// Constructs an instance of this class given a single state.
    /// </summary>
    public StateProfile(IState state)
    {
        StatesAndStepFunctions = new List<(IState, IList<IStepFunction>)>()
        {
            (state, Array.Empty<IStepFunction>())
        };
    }

    /// <summary>
    /// Constructs an instance of this class given a set of states.
    /// </summary>
    /// <param name="states"></param>
    public StateProfile(IList<IState> states)
    {
        StatesAndStepFunctions =
            states.Select(s => (s, (IList<IStepFunction>)Array.Empty<IStepFunction>())).ToList();
    }

    /// <summary>
    /// Constructs an instance of this class given a set of states and associated
    /// step functions.
    /// </summary>
    public StateProfile(IList<(IState, IList<IStepFunction>)> statesAndStepFunctions)
    {
        StatesAndStepFunctions = statesAndStepFunctions;

        // If any of the step functions is null, then convert that to an empty list,
        // while preserving the non-null ones.
        if (StatesAndStepFunctions.Any(ssf => ssf.StepFunctions == null))
        {
            StatesAndStepFunctions = StatesAndStepFunctions
                .Select(ssf => (
                    ssf.State,
                    ssf.StepFunctions == null ? Array.Empty<IStepFunction>() : ssf.StepFunctions))
                .ToList();
        }
    }

    /// <summary>
    /// This method returns the single next state but only if the set of next
    /// states contains a single state. It throws the <see cref="MultipleStateException"/>
    /// exception otherwise.
    /// </summary>
    public IState SingleState()
    {
        Invariant.Assert(StatesAndStepFunctions.Count > 0);

        if (StatesAndStepFunctions.Count != 1 ||
            StatesAndStepFunctions[0].StepFunctions.Count != 0)
        {
            throw new MultipleStateException();
        }

        return StatesAndStepFunctions.Single().State;
    }

    /// <summary>
    /// This method indicates if a system is uniquely in a single state or if it could be in
    /// more than one possible state.
    /// </summary>
    public bool IsSingleState()
    {
        Invariant.Assert(StatesAndStepFunctions.Count > 0);

        if (StatesAndStepFunctions.Count != 1 ||
            StatesAndStepFunctions[0].StepFunctions.Count != 0)
        {
            return false;
        }

        return true;
    }

    public override string ToString()
    {
        if (IsSingleState())
        {
            return SingleState().ToString();
        }
        else
        {
            var statesAndStepFunctionStrings =
                StatesAndStepFunctions.Select(ssf =>
                {
                    var stepFunctionStrings = string.Join(", ", ssf.StepFunctions.Select(sf => sf.StepFunctionId));
                    return $"(state: {ssf.State}, step functions: [{stepFunctionStrings}])";
                });

            return string.Join("; ", statesAndStepFunctionStrings);
        }
    }
}
