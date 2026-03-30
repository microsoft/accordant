// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TerminationDetection.StateDefinition
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    [StateDefinition]
    public class TokenState
    {
        public int NodeIndex { get; set; }

        public int Q { get; set; }

        public Color Color { get; set; }
    }

    [StateDefinition]
    public class NodeState
    {
        public bool Active { get; set; }

        public Color Color { get; set; }

        public int Pending { get; set; }

        public int Counter { get; set; }
    }

    [StateDefinition]
    public class SystemState
    {
        public List<NodeState> Nodes { get; set; }

        public TokenState Token { get; set; }
    }
}
