// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;

/// <summary>
/// This class contains options and configurations that control
/// the state space visualization.
/// </summary>
public class VisualizationOptions
{
    /// <summary>
    /// This lambda creates a label for a given state graph node. It is called
    /// _exactly once_ for a given state graph node. It is helpful to be reminded
    /// that a state graph node consists of a state and a set of step of functions.
    /// It is thus possible that multiple state graph nodes may contain the same state
    /// (but differ in their set of step functions). In that case, the lambda will
    /// be called multiple times with the same state, but only once for each state graph
    /// node.
    /// </summary>
    public Func<StateGraphNode, string> NodeLabelLambda { get; set; } = StateGraphNode.DefaultNodeLabelLambda;

    /// <summary>
    /// Setting this property to true create a count-based state serializer.
    /// Count-based state serializer is preferable in situations where the state
    /// string representation is too large and we want a more compact representation
    /// instead.
    /// </summary>
    public bool UseCountBasedNodeLabels
    {
        set
        {
            if (value)
            {
                NodeLabelLambda = StateGraphNode.CreateCountBasedNodeLabelLambda();
            }
        }
    }
}
