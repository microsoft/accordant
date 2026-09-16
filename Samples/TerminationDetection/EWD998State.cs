namespace TerminationDetection
{
    using System.Collections.Generic;
    using Microsoft.Accordant;

    [State]
    public partial class TokenState : State
    {
        public int NodeIndex { get; set; }

        public int Q { get; set; }

        public Color Color { get; set; }
    }

    [State]
    public partial class NodeState : State
    {
        public bool Active { get; set; }

        public Color Color { get; set; }

        public int Pending { get; set; }

        public int Counter { get; set; }
    }

    [State]
    public partial class SystemState : State
    {
        public List<NodeState> Nodes { get; set; } = new();

        public TokenState Token { get; set; }
    }
}
