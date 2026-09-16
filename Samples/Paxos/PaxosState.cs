namespace Accordant.Samples.Paxos
{
    using Microsoft.Accordant;

    /// <summary>
    /// Global state of the bounded single-decree Paxos model.
    /// <see cref="Paxos.P"/> proposers and <see cref="Paxos.A"/> acceptors;
    /// each proposer has a fixed ballot and a fixed preferred value.
    /// The model bakes the preferred value into the proposer id to keep
    /// the state space small (no separate Init step needed).
    /// </summary>
    [State]
    public partial class PaxosState : State
    {
        // --- Proposer state -----------------------------------------------
        public Paxos.ProposerPhase[] Phase { get; set; }
        /// <summary>Value the proposer will use in Phase2 (set when entering Promised).</summary>
        public int[] ProposedValue { get; set; }
        /// <summary>Bitmask over acceptors: which have promised this proposer.</summary>
        public int[] PreparedMask { get; set; }
        /// <summary>Bitmask over acceptors: which have accepted this proposer's value.</summary>
        public int[] AcceptedMask { get; set; }

        // --- Acceptor state -----------------------------------------------
        /// <summary>Highest ballot promised so far (0 = none).</summary>
        public int[] Promised { get; set; }
        /// <summary>Ballot of the last accepted proposal (0 = none).</summary>
        public int[] AcceptedBallot { get; set; }
        /// <summary>Value of the last accepted proposal (0 = none).</summary>
        public int[] AcceptedValue { get; set; }

        public Paxos.PaxosOp LastOp { get; set; }
        public int LastProposer { get; set; }
        public int LastAcceptor { get; set; }
    }
}

