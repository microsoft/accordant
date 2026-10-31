namespace TerminationDetection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    public enum Color { Black, White }

    /// <summary>
    /// This class models the termination detection protocol described in
    /// EWD998 (https://www.cs.utexas.edu/users/EWD/ewd09xx/EWD998.PDF),
    /// where the leader in a set of nodes detects whether
    /// the distributed computation being conducted by the nodes has terminated. The problem is
    /// made interesting as active nodes can send messages to passive nodes, this "waking" them
    /// again. Message sends and receives are asynchronous, though the specification does assume
    /// that sent messages are always received, even if with an arbitrary delay.
    /// 
    /// The specification below is a transcription of the specification of this protocol in TLA+.
    /// See the TLA+ spec at https://github.com/lemmy/ewd998/blob/main/EWD998.tla
    /// in the repository at https://github.com/lemmy/ewd998.
    /// </summary>
    public class EWD998
    {
        /// <summary>
        /// This constant models the number of nodes in the system. Larger values
        /// of N take a longer time to check. Smaller values are often sufficient
        /// to find tricky bugs
        /// (also known as the small scale hypothesis:
        /// http://projects.csail.mit.edu/mulsaw/papers/SSH.ps)
        /// </summary>
        public const int N = 2;

        /// <summary>
        /// This step passes the token from the leader (node at index 0) to the
        /// last node (node at index N - 1).
        /// </summary>
        public class InitiateProbeStep : TLAStepFunction
        {
            public override string StepFunctionId => "InitiateProbe";

            /// <summary>
            /// This step is enabled if the token is at the leader
            /// and it detects termination hasn't happened by inspecting
            /// the token's state and it's own state.
            /// </summary>
            public override bool IsEnabled(SystemState systemState)
            {
                var token = systemState.Token;
                var leaderNode = systemState.Nodes[0];

                return
                    token.NodeIndex == 0 &&
                    (token.Color == Color.Black ||
                     leaderNode.Color == Color.Black ||
                     leaderNode.Counter + token.Q > 0);
            }

            /// <summary>
            /// Transition the system to the next state where the token is at node
            /// at index N-1.
            /// </summary>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextState = (SystemState)systemState.Clone();

                var nextToken = nextState.Token;
                nextToken.NodeIndex = N - 1;
                nextToken.Q = 0;
                nextToken.Color = Color.White;

                nextState.Nodes[0].Color = Color.White;

                return new SystemState[] { nextState };
            }
        }

        /// <summary>
        /// This step passes the token to the node at index i-1 if this
        /// node is not active anymore.
        /// </summary>
        public class PassTokenStep : TLAStepFunction
        {
            private int nodeIndex;

            public PassTokenStep(int nodeIndex)
            {
                this.nodeIndex = nodeIndex;
            }

            public override string StepFunctionId => $"PassToken_{nodeIndex}";

            /// <summary>   
            /// This step is enabled if the token is at this node and it is not active.
            /// </summary>
            /// <param name="systemState"></param>
            /// <returns></returns>
            public override bool IsEnabled(SystemState systemState)
            {
                var token = systemState.Token;
                var node = systemState.Nodes[nodeIndex];

                return
                    !node.Active &&
                    token.NodeIndex == nodeIndex;
            }

            /// <summary>
            /// Pass the token to the node at index i-1, updating the state of the
            /// token based on this node's state.
            /// </summary>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextState = (SystemState)systemState.Clone();

                var node = systemState.Nodes[nodeIndex];

                var nextToken = nextState.Token;
                nextToken.NodeIndex -= 1;
                nextToken.Q += node.Counter;
                nextToken.Color = node.Color == Color.Black ? Color.Black : node.Color;

                nextState.Nodes[nodeIndex].Color = Color.White;

                return new SystemState[] { nextState };
            }
        }

        /// <summary>
        /// This step sends a message to another node, whether it be active or passive.
        /// If a passive node receives a message, it gets active again.
        /// </summary>
        public class SendMessageStep : TLAStepFunction
        {
            private int nodeIndex;

            public SendMessageStep(int nodeIndex)
            {
                this.nodeIndex = nodeIndex;
            }

            public override string StepFunctionId => $"SendMessage_{nodeIndex}";

            /// <summary>
            /// This step is enabled if the node is active.
            /// </summary>
            public override bool IsEnabled(SystemState systemState)
            {
                return
                    systemState.Nodes[nodeIndex].Active;
            }

            /// <summary>
            /// A node can send a message to any other node, except itself.
            /// We need to explore all possibilities where a message can be sent
            /// to any node. This step therefore produces N-1 next states, where
            /// a message is sent to a different node in each possible future.
            /// </summary>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextStates = new List<SystemState>();

                for (int i = 0; i < N; i++)
                {
                    if (i == nodeIndex)
                    {
                        continue;
                    }

                    var nextState = (SystemState)systemState.Clone();

                    var nextThisNode = nextState.Nodes[nodeIndex];
                    nextThisNode.Counter += 1;

                    var receivingNode = nextState.Nodes[i];
                    receivingNode.Pending += 1;

                    nextStates.Add(nextState);
                }

                return nextStates;
            }
        }

        /// <summary>
        /// This steps receives a message sent from another node.
        /// Message send and receive are async processes which is why the
        /// reception of the message is modeled as a separate step from
        /// sending the message.
        /// </summary>
        public class ReceiveMessageStep : TLAStepFunction
        {
            private int nodeIndex;

            public ReceiveMessageStep(int nodeIndex)
            {
                this.nodeIndex = nodeIndex;
            }

            public override string StepFunctionId => $"ReceiveMessage_{nodeIndex}";

            /// <summary>
            /// This step is enabled if it's pending counter is greater than
            /// zero. The pending count indicates "messages in flight" destined
            /// for this node. A node need not be active to receive a message,
            /// but does become active when the message is received.
            /// </summary>
            public override bool IsEnabled(SystemState systemState)
            {
                return systemState.Nodes[nodeIndex].Pending > 0;
            }

            /// <summary>
            /// Transitions to a state in which the message has been received.
            /// The reception of the message marks the node as active.
            /// </summary>
            /// <param name="systemState"></param>
            /// <returns></returns>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextState = (SystemState)systemState.Clone();

                var nextNode = nextState.Nodes[nodeIndex];
                nextNode.Active = true;
                nextNode.Pending -= 1;
                nextNode.Counter -= 1;
                nextNode.Color = Color.Black;

                return new SystemState[] { nextState };
            }
        }

        /// <summary>
        /// This step deactivates a single node — the one identified by
        /// <see cref="NodeIndex"/>. Splitting the deactivation per node
        /// (rather than emitting all 2^M active subsets in one global
        /// step) lets fairness predicates target a specific node. In
        /// particular, strong fairness on every
        /// <c>DeactivateStep(i)</c> forces an active node to eventually
        /// go passive, which is required to express token-traversal
        /// liveness (□◇ TokenAtLeader).
        /// </summary>
        public class DeactivateStep : TLAStepFunction
        {
            public int NodeIndex { get; }

            public DeactivateStep(int nodeIndex)
            {
                NodeIndex = nodeIndex;
            }

            public override string StepFunctionId => $"Deactivate_{NodeIndex}";

            /// <summary>
            /// Enabled iff the targeted node is currently active.
            /// </summary>
            public override bool IsEnabled(SystemState systemState)
            {
                return systemState.Nodes[NodeIndex].Active;
            }

            /// <summary>
            /// Single next-state: clone and mark this one node passive.
            /// All 2^M "subset of nodes deactivate" combinations are
            /// reachable via sequential applications of per-node steps,
            /// so the reachable state space is preserved.
            /// </summary>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextState = (SystemState)systemState.Clone();
                nextState.Nodes[NodeIndex].Active = false;
                return new SystemState[] { nextState };
            }
        }

        /// <summary>
        /// This property encodes all the nodes in the system as having
        /// terminated (i.e. none of the nodes in the system are active
        /// and there are no messages in flight)
        /// </summary>
        public static bool HasSystemTerminated(IState state)
        {
            var systemState = (SystemState)state;
            return systemState.Nodes.All(n => !n.Active && n.Pending == 0);
        }

        /// <summary>
        /// This property encodes the leader "detecting" whether the system
        /// has terminated. The leader can leverage the token's state (when the
        /// token is at the leader) and its own state to make this determination.
        /// </summary>
        public static bool TerminationDetected(IState state)
        {
            var systemState = (SystemState)state;

            var token = systemState.Token;
            var leaderNode = systemState.Nodes[0];

            return token.NodeIndex == 0 &&
                token.Color == Color.White &&
                (token.Q + leaderNode.Counter == 0) &&
                leaderNode.Color == Color.White &&
                !leaderNode.Active;
        }

        /// <summary>
        /// Returns the canonical list of step functions exercised by the
        /// EWD998 model. Mirrors the inline setup used by
        /// <see cref="Tests.TerminationDetectionModelChecking"/>.
        /// </summary>
        public static IList<IStepFunction> AllSteps() => BuildSteps(buggy: false);

        /// <summary>
        /// Bug-injection variant: replaces every <see cref="ReceiveMessageStep"/>
        /// with a <see cref="BuggyReceiveMessageStep"/> that forgets to
        /// mark the receiving node black. The token can then traverse a
        /// node that just received a message without picking up its
        /// taint, and the leader can spuriously decide that termination
        /// has occurred mid-conversation.
        /// </summary>
        public static IList<IStepFunction> AllStepsBuggy() => BuildSteps(buggy: true);

        private static IList<IStepFunction> BuildSteps(bool buggy)
        {
            var steps = new List<IStepFunction>();
            steps.Add(new InitiateProbeStep());
            for (int i = 0; i < N; i++)
            {
                if (i != 0)
                    steps.Add(new PassTokenStep(i));
                steps.Add(new SendMessageStep(i));
                steps.Add(buggy ? (TLAStepFunction)new BuggyReceiveMessageStep(i) : new ReceiveMessageStep(i));
                steps.Add(new DeactivateStep(i));
            }
            return steps;
        }

        /// <summary>
        /// Returns the canonical initial system state: leader holds a
        /// black token at <c>Q=0</c>; every node is active, white, with
        /// counter 0 and no pending messages.
        /// </summary>
        public static SystemState InitialState()
        {
            var token = new TokenState { NodeIndex = 0, Q = 0, Color = Color.Black };
            var nodes = new List<NodeState>();
            for (int i = 0; i < N; i++)
                nodes.Add(new NodeState { Active = true, Pending = 0, Color = Color.White, Counter = 0 });
            return new SystemState { Nodes = nodes, Token = token };
        }

        /// <summary>
        /// Bug-injection variant of <see cref="ReceiveMessageStep"/>: bumps
        /// the receiver's <see cref="NodeState.Active"/> / counter / pending
        /// state as usual but forgets to set its
        /// <see cref="NodeState.Color"/> to <see cref="Color.Black"/>.
        /// A token round that passes this node after the receive but
        /// before any further send therefore picks up no taint, and the
        /// leader can spuriously declare termination.
        /// </summary>
        public class BuggyReceiveMessageStep : TLAStepFunction
        {
            private readonly int nodeIndex;
            public BuggyReceiveMessageStep(int nodeIndex) { this.nodeIndex = nodeIndex; }
            public override string StepFunctionId => $"ReceiveMessage_{nodeIndex}";
            public override bool IsEnabled(SystemState systemState)
                => systemState.Nodes[nodeIndex].Pending > 0;
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var nextState = (SystemState)systemState.Clone();
                var nextNode = nextState.Nodes[nodeIndex];
                nextNode.Active = true;
                nextNode.Pending -= 1;
                nextNode.Counter -= 1;
                // BUG: forget to set Color = Black.
                return new SystemState[] { nextState };
            }
        }
    }

    public class Tests
    {
        [Test]
        public static void TerminationDetectionModelChecking()
        {
            //
            // 1. Instantiate steps.
            //

            // We'll start the simulation by declaring steps that
            // can be taken.
            var steps = new List<IStepFunction>();

            // Step through which the leader starts a token passing
            // round.
            steps.Add(new EWD998.InitiateProbeStep());

            // A pass token step for each node, but the leader,
            // and send and receive steps for each node.
            for (int i = 0; i < EWD998.N; i++)
            {
                if (i != 0)
                {
                    steps.Add(new EWD998.PassTokenStep(i));
                }

                steps.Add(new EWD998.SendMessageStep(i));
                steps.Add(new EWD998.ReceiveMessageStep(i));
            }

            // Step which de-actives nodes — one instance per node.
            for (int i = 0; i < EWD998.N; i++)
            {
                steps.Add(new EWD998.DeactivateStep(i));
            }

            //
            // 2. Define initial state of the system.
            //

            // The token starts out at leader.
            var token = new TokenState()
            {
                NodeIndex = 0,
                Q = 0,
                Color = Color.Black
            };

            // Each node starts out in active state.
            var nodes = new List<NodeState>();
            for (int i = 0; i < EWD998.N; i++)
            {
                nodes.Add(new NodeState()
                {
                    Active = true,
                    Pending = 0,
                    Color = Color.White,
                    Counter = 0
                });
            }

            // The full system state comprising of node states
            // and token state.
            var initialState = new SystemState()
            {
                Nodes = nodes,
                Token = token
            };

            //
            // 3. Explore all possible evolutions of the system.
            //    The system can evolve infinitely so we bound it
            //    by bounding the number of message sends and receives.
            //
            var rootNode = StateGraph.ExploreStateGraph(
                steps,
                initialState,
                stateConstraint: (s) =>
                {
                    var systemState = (SystemState)s;

                    return
                        systemState.Nodes.All(n => n.Counter < 3 && n.Pending < 3) &&
                        systemState.Token.Q < 3;

                });

            //
            // 4. Check safety and liveness properties after the simulation.
            //

            // Safety condition: It's always true that when the leader detects termination,
            // the system is in fact in a terminated state.
            var safetyFormula = LtlFormula.Always(
                LtlFormula.Implies(
                    LtlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected"),
                    LtlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated")));
            var safetyResult = LtlCheck.Check(rootNode, safetyFormula);

            Assert.IsTrue(safetyResult.Valid, safetyResult.GetTraceString());

            // Liveness condition: When the system reaches a terminated state,
            // the leader _eventually_ detects termination.
            var livenessFormula = LtlFormula.LeadsTo(
                LtlFormula.Prop(EWD998.HasSystemTerminated, "HasSystemTerminated"),
                LtlFormula.Prop(EWD998.TerminationDetected, "TerminationDetected"));
            var livenessResult = LtlCheck.Check(rootNode, livenessFormula);

            Assert.IsTrue(livenessResult.Valid, livenessResult.GetTraceString());
        }
    }
}
