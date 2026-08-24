namespace DiningPhilosophers
{
    using Microsoft.Accordant;

    /// <summary>Program-counter values for one philosopher.</summary>
    public enum PhilPC
    {
        /// <summary>Idling between meals.</summary>
        Thinking,
        /// <summary>Wants to eat but holds no fork yet.</summary>
        Hungry,
        /// <summary>Holds the first fork in this philosopher's pickup order.</summary>
        HoldOne,
        /// <summary>Holds both forks and is eating.</summary>
        Eating,
    }

    /// <summary>
    /// Global state for the three-philosopher dining table.
    /// </summary>
    [State]
    public partial class DiningState
    {
        /// <summary>Per-philosopher program counters.</summary>
        public PhilPC PC0 { get; set; }
        public PhilPC PC1 { get; set; }
        public PhilPC PC2 { get; set; }

        /// <summary>
        /// Fork ownership. <c>-1</c> means free; otherwise the index of the
        /// philosopher currently holding the fork. Fork <c>i</c> sits between
        /// philosophers <c>i</c> and <c>(i-1) mod 3</c> in the obvious round-
        /// table layout — but every philosopher's pickup logic refers to
        /// forks by index via <see cref="Dining"/>, so the geometry is
        /// abstracted away here.
        /// </summary>
        public int F0 { get; set; }
        public int F1 { get; set; }
        public int F2 { get; set; }

    }
}
