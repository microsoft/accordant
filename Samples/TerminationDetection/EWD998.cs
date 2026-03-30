// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TerminationDetection
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    public enum Color { Black, White }

    /// <summary>
    /// This class models the <see cref="https://www.cs.utexas.edu/users/EWD/ewd09xx/EWD998.PDF">
    /// termination detection</see> protocol where the leader in a set of nodes detects whether
    /// the distributed computation being conducted by the nodes has terminated. The problem is
    /// made interesting as active nodes can send messages to passive nodes, this "waking" them
    /// again. Message sends and receives are asynchronous, though the specification does assume
    /// that sent messages are always received, even if with an arbitrary delay.
    /// 
    /// The specification below is a transcription of the specification of this protocol in TLA+.
    /// See the <see cref="https://github.com/lemmy/ewd998/blob/main/EWD998.tla">TLA+ spec</see> in this
    /// <see cref="https://github.com/lemmy/ewd998">repository</see>.
    /// </summary>
    public class EWD998
    {
        /// <summary>
        /// This constant models the number of nodes in the system. Larger values
        /// of N take a longer time to check. Smaller values are often sufficient
        /// to find tricky bugs
        /// (also known as <see cref="http://projects.csail.mit.edu/mulsaw/papers/SSH.ps">small scale hypothesis</see>)
        /// </summary>
        public const int N = 2;

        /// <summary>
        /// This step passes the token from the leader (node at index 0) to the
        /// last node (node at index N - 1).
        /// </summary>
        public class InitiateProbeStep : TLAStepFunction
        {
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
        /// This step deactivates a set of active nodes.
        /// </summary>
        public class DeactivateStep : TLAStepFunction
        {
            /// <summary>
            /// This step is always enabled.
            /// </summary>
            public override bool IsEnabled(SystemState systemState)
            {
                return true;
            }

            /// <summary>
            /// More than one active node can be de-activated in a single state
            /// transition. If there are M active nodes, there are 2^M different
            /// combinations of nodes that might be deactivated. This method
            /// generates 2^M next states, where each state has a unique combination
            /// of nodes deactivated. We essentially exhaustively all possible combinations
            /// of set of nodes deactivating together.
            /// </summary>
            public override IList<SystemState> NextStates(SystemState systemState)
            {
                var numActiveNodeIndices =
                    systemState.Nodes
                    .Select((n, i) => (n, i))
                    .Where(ni => ni.n.Active)
                    .Select(ni => ni.i)
                    .ToList();

                var nextStates = new List<SystemState>();

                var combinations = (int)Math.Pow(numActiveNodeIndices.Count, 2);
                for (int bitmask = 0; bitmask < combinations; bitmask++)
                {
                    var nextState = (SystemState)systemState.Clone();

                    for (int i = 0; i < numActiveNodeIndices.Count; i++)
                    {
                        if ((bitmask & (1 << i)) != 0)
                        {
                            var nextNode = nextState.Nodes[i];
                            nextNode.Active = false;
                        }
                    }

                    nextStates.Add(nextState);
                }

                return nextStates;
            }
        }

        /// <summary>
        /// This property encodes all the nodes in the system as having
        /// terminated (i.e. none of the nodes in the system are active
        /// and there are no messages in flight)
        /// </summary>
        public static bool HasSystemTerminated(State state)
        {
            var systemState = (SystemState)state;
            return systemState.Nodes.All(n => !n.Active && n.Pending == 0);
        }

        /// <summary>
        /// This property encodes the leader "detecting" whether the system
        /// has terminated. The leader can leverage the token's state (when the
        /// token is at the leader) and its own state to make this determination.
        /// </summary>
        public static bool TerminationDetected(State state)
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

            // Step which de-actives a set of active nodes.
            steps.Add(new EWD998.DeactivateStep());

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
            var nodes = new ListState<NodeState>();
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
            var safetyResult = Property.Check(c => c.Always(
                rootNode,
                (n) => EWD998.TerminationDetected(n.State) ?
                    EWD998.HasSystemTerminated(n.State) :
                    true));

            Assert.IsTrue(safetyResult.Valid);

            // Liveness condition: It's always true that when the system reaches a terminated
            // state, the leader _eventually_ detects termination.

            var livenessResult = Property.Check(c => c.Always(
                rootNode,
                (n) => EWD998.HasSystemTerminated(n.State) ?
                    c.Eventually(n, (nextN) => EWD998.TerminationDetected(nextN.State)) :
                    true));

            Assert.IsTrue(livenessResult.Valid);
        }
    }
}
