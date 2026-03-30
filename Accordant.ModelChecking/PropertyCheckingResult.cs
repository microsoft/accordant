// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of checking a property over a state graph.
    /// </summary>
    public class PropertyCheckingResult
    {
        /// <summary>
        /// This property indicates whether the property is valid or not.
        /// </summary>
        public bool Valid { get; set; }

        /// <summary>
        /// This property contains the trace that leads to the violation of the property.
        /// The Trace is null if the property is valid.
        /// </summary>
        public List<TraceItem> Trace { get; set; }

        /// <summary>
        /// This method returns the <see cref="Trace"/> as a helpful string for easy inspection.
        /// </summary>
        public string GetTraceString()
        {
            if (Trace == null)
            {
                return "No trace as property check succeeded.";
            }

            var list = new List<string>();
            foreach (var traceItem in Trace)
            {
                var action = traceItem.StepFunction == null ?
                    "Start" :
                    traceItem.StepFunction.StepFunctionId;


                list.Add($"--{action}--> {traceItem.StateGraphNode.State}");
            }

            return string.Join("\n", list);
        }
    }
}
