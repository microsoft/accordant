namespace Peterson
{
    using Microsoft.Accordant;

    /// <summary>
    /// Program-counter values for one process in Peterson's two-process
    /// mutual-exclusion algorithm. Each process repeats the sequence
    /// <c>NCS → SetFlag → SetTurn → Wait → CS → Exit → NCS …</c>
    /// </summary>
    public enum PetersonPC
    {
        /// <summary>Non-critical section (idle).</summary>
        NCS,
        /// <summary>About to execute <c>flag[i] := true</c>.</summary>
        SetFlag,
        /// <summary>About to execute <c>turn := 1-i</c>.</summary>
        SetTurn,
        /// <summary>Busy-waiting: while <c>flag[j] ∧ turn = j</c>.</summary>
        Wait,
        /// <summary>In the critical section.</summary>
        CS,
        /// <summary>About to execute <c>flag[i] := false</c>.</summary>
        Exit,
    }

    /// <summary>
    /// Global state for Peterson's algorithm with two processes (i = 0, 1).
    /// </summary>
    [State]
    public partial class PetersonState
    {
        /// <summary><c>flag[0]</c> — process 0 wants the critical section.</summary>
        public bool Flag0 { get; set; }
        /// <summary><c>flag[1]</c> — process 1 wants the critical section.</summary>
        public bool Flag1 { get; set; }
        /// <summary><c>turn</c> ∈ {0, 1} — shared turn variable.</summary>
        public int Turn { get; set; }
        /// <summary>Process 0's program counter.</summary>
        public PetersonPC PC0 { get; set; }
        /// <summary>Process 1's program counter.</summary>
        public PetersonPC PC1 { get; set; }
    }
}
