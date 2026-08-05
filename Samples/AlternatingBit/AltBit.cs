namespace AlternatingBit
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// Alternating-Bit Protocol (Bartlett, Scantlebury &amp; Wilkinson, 1969)
    /// between a single sender and a single receiver, over two capacity-1
    /// lossy channels. Each transmission carries a 1-bit sequence number
    /// that flips on every successful delivery, so the receiver can tell
    /// fresh data from retransmissions.
    /// <para>
    /// To keep exploration finite the sender is bounded to
    /// <see cref="MaxMessages"/> distinct payloads. Once
    /// <c>NextPayload == MaxMessages</c> and both channels are empty, a
    /// <see cref="StutterStep"/> self-loop keeps the state graph non-terminating
    /// (so every state has an outgoing edge — required by the underlying
    /// cycle-detection machinery).
    /// </para>
    /// </summary>
    public static class AltBit
    {
        /// <summary>Number of distinct payloads the sender transmits.</summary>
        public const int MaxMessages = 3;

        // --- Atomic predicates --------------------------------------------

        /// <summary><c>Delivered</c> is a prefix of <c>[0, 1, …, MaxMessages-1]</c>.</summary>
        public static bool InOrder(IState s)
        {
            var st = (AltBitState)s;
            for (int i = 0; i < st.Delivered.Length; i++)
                if (st.Delivered[i] != i) return false;
            return true;
        }

        /// <summary>All <see cref="MaxMessages"/> payloads have been delivered.</summary>
        public static bool AllDelivered(IState s)
            => ((AltBitState)s).Delivered.Length == MaxMessages;

        /// <summary>Sender currently holds bit 0.</summary>
        public static bool SenderBit0(IState s) => ((AltBitState)s).SenderBit == 0;
        /// <summary>Sender currently holds bit 1.</summary>
        public static bool SenderBit1(IState s) => ((AltBitState)s).SenderBit == 1;

        // --- State-graph construction ------------------------------------

        /// <summary>
        /// Initial state: both bits at 0, both channels empty, nothing
        /// delivered yet.
        /// </summary>
        public static AltBitState InitialState() => new AltBitState
        {
            SenderBit = 0,
            ReceiverBit = 0,
            DataChanHas = false,
            AckChanHas = false,
            Delivered = Array.Empty<int>(),
            NextPayload = 0,
        };

        /// <summary>The full list of step functions exercised by the model.</summary>
        public static IList<IStepFunction> AllSteps() => new IStepFunction[]
        {
            new SendStep(),
            new LoseDataStep(),
            new ReceiveStep(),
            new LoseAckStep(),
            new ReceiveAckStep(),
            new StutterStep(),
        };

        /// <summary>
        /// Bug-injection variant: replaces <see cref="ReceiveStep"/> with
        /// <see cref="BuggyReceiveStep"/>, which delivers every in-flight
        /// payload regardless of whether the data-channel bit matches the
        /// expected <see cref="AltBitState.ReceiverBit"/>. Duplicates get
        /// re-delivered, so <c>Delivered</c> no longer respects the
        /// in-order prefix discipline asserted by <see cref="InOrder"/>.
        /// </summary>
        public static IList<IStepFunction> AllStepsBuggy() => new IStepFunction[]
        {
            new SendStep(),
            new LoseDataStep(),
            new BuggyReceiveStep(),
            new LoseAckStep(),
            new ReceiveAckStep(),
            new StutterStep(),
        };

        // --- Step functions ----------------------------------------------

        /// <summary>Common scaffolding for an AltBit step.</summary>
        public abstract class AltBitStep : BaseStepFunction
        {
            /// <summary>Stable id keyed on the concrete type name — see Peterson sample for rationale.</summary>
            public override string StepFunctionId => GetType().Name;

            public abstract bool IsEnabled(AltBitState s);
            public abstract AltBitState Apply(AltBitState s);

            protected override IList<StepResult> ApplyInternal(IState state)
            {
                var s = (AltBitState)state;
                if (!IsEnabled(s)) return null;
                return new[]
                {
                    new StepResult
                    {
                        State = Apply(s),
                        StepFunctions = new IStepFunction[] { this },
                    }
                };
            }
        }

        /// <summary>
        /// Sender posts <c>(SenderBit, NextPayload)</c> on the empty data
        /// channel. Retransmission is modeled by the combination of
        /// <see cref="LoseDataStep"/> emptying the channel and this step
        /// firing again with the same un-acked payload.
        /// </summary>
        public sealed class SendStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s)
                => !s.DataChanHas && s.NextPayload < MaxMessages;

            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                n.DataChanHas = true;
                n.DataChanBit = s.SenderBit;
                n.DataChanPayload = s.NextPayload;
                return n;
            }
        }

        /// <summary>Lossy S→R channel drops the in-flight message.</summary>
        public sealed class LoseDataStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s) => s.DataChanHas;
            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                n.DataChanHas = false;
                return n;
            }
        }

        /// <summary>
        /// Receiver consumes the in-flight message. If its bit matches the
        /// currently-expected <see cref="AltBitState.ReceiverBit"/> the
        /// payload is appended to <see cref="AltBitState.Delivered"/>, the
        /// receiver flips its expected bit, and an ack carrying the bit
        /// just accepted is posted (overwriting any pending ack). If the
        /// bit does <em>not</em> match (a duplicate of the previously
        /// accepted message), the receiver simply re-acks the previous
        /// successful bit. In either case the data channel is emptied.
        /// </summary>
        public sealed class ReceiveStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s) => s.DataChanHas;
            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                if (s.DataChanBit == s.ReceiverBit)
                {
                    var d = new int[s.Delivered.Length + 1];
                    Array.Copy(s.Delivered, d, s.Delivered.Length);
                    d[s.Delivered.Length] = s.DataChanPayload;
                    n.Delivered = d;
                    n.AckChanHas = true;
                    n.AckChanBit = s.ReceiverBit; // ack with bit we just accepted
                    n.ReceiverBit = 1 - s.ReceiverBit;
                }
                else
                {
                    n.AckChanHas = true;
                    n.AckChanBit = 1 - s.ReceiverBit; // re-ack the previously accepted bit
                }
                n.DataChanHas = false;
                return n;
            }
        }

        /// <summary>
        /// Bug-injection variant of <see cref="ReceiveStep"/>: drops the
        /// sequence-number check (<c>DataChanBit == ReceiverBit</c>) and
        /// always appends the in-flight payload, flips the receiver bit
        /// and acks. Duplicates make it into <see cref="AltBitState.Delivered"/>
        /// (e.g. the first payload landing twice), so the
        /// <see cref="InOrder"/> safety property fails.
        /// </summary>
        public sealed class BuggyReceiveStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s) => s.DataChanHas;
            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                var d = new int[s.Delivered.Length + 1];
                Array.Copy(s.Delivered, d, s.Delivered.Length);
                d[s.Delivered.Length] = s.DataChanPayload;
                n.Delivered = d;
                n.AckChanHas = true;
                n.AckChanBit = s.ReceiverBit;
                n.ReceiverBit = 1 - s.ReceiverBit;
                n.DataChanHas = false;
                return n;
            }
            // Mirror the canonical id so fairness-keyed analyses treat
            // this as the same enabling family.
            public override string StepFunctionId => nameof(ReceiveStep);
        }

        /// <summary>Lossy R→S channel drops the in-flight ack.</summary>
        public sealed class LoseAckStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s) => s.AckChanHas;
            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                n.AckChanHas = false;
                return n;
            }
        }

        /// <summary>
        /// Sender consumes the in-flight ack. A matching ack flips the
        /// sender bit and bumps <see cref="AltBitState.NextPayload"/>;
        /// a stale ack is silently dropped. Either way the ack channel is
        /// emptied.
        /// </summary>
        public sealed class ReceiveAckStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s) => s.AckChanHas;
            public override AltBitState Apply(AltBitState s)
            {
                var n = (AltBitState)s.Clone();
                if (s.AckChanBit == s.SenderBit)
                {
                    n.SenderBit = 1 - s.SenderBit;
                    n.NextPayload = s.NextPayload + 1;
                }
                n.AckChanHas = false;
                return n;
            }
        }

        /// <summary>
        /// Self-loop in the absorbing "all done" state. The state graph
        /// otherwise has no outgoing edge once the sender has exhausted
        /// <see cref="MaxMessages"/> and both channels are empty; the
        /// stutter step makes that state participate in cycles so the
        /// cycle-detection machinery treats reaching it as an infinite
        /// trace satisfying any property that already holds there. It is
        /// <em>not</em> covered by any fairness predicate.
        /// </summary>
        public sealed class StutterStep : AltBitStep
        {
            public override bool IsEnabled(AltBitState s)
                => s.NextPayload == MaxMessages && !s.DataChanHas && !s.AckChanHas;
            public override AltBitState Apply(AltBitState s) => (AltBitState)s.Clone();
        }
    }
}
