namespace AlternatingBit
{
    using Microsoft.Accordant;

    /// <summary>
    /// Global state for the Alternating-Bit Protocol with capacity-1 lossy
    /// channels in both directions.
    /// </summary>
    [State]
    public partial class AltBitState
    {
        /// <summary>The bit the sender is currently trying to deliver.</summary>
        public int SenderBit { get; set; }
        /// <summary>The bit the receiver is currently expecting.</summary>
        public int ReceiverBit { get; set; }

        /// <summary>True iff the S→R channel currently holds a message.</summary>
        public bool DataChanHas { get; set; }
        /// <summary>Bit tag of the in-flight message (only meaningful when <see cref="DataChanHas"/>).</summary>
        public int DataChanBit { get; set; }
        /// <summary>Payload of the in-flight message (only meaningful when <see cref="DataChanHas"/>).</summary>
        public int DataChanPayload { get; set; }

        /// <summary>True iff the R→S channel currently holds an ack.</summary>
        public bool AckChanHas { get; set; }
        /// <summary>Bit value of the in-flight ack (only meaningful when <see cref="AckChanHas"/>).</summary>
        public int AckChanBit { get; set; }

        /// <summary>
        /// Sequence of payload identifiers handed up to the application so
        /// far, in delivery order. Always a prefix of <c>[0, 1, …,
        /// AltBit.MaxMessages-1]</c> in a correct protocol — exactly what
        /// the safety property checks.
        /// </summary>
        public int[] Delivered { get; set; }

        /// <summary>Next fresh payload id the sender will use (0-indexed).</summary>
        public int NextPayload { get; set; }
    }
}
