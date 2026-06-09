namespace DiningPhilosophers
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    /// <summary>
    /// Sanity-check: confirms the naive variant's classical deadlock state
    /// is actually reachable in the explored graph, and the asymmetric
    /// variant has no <see cref="Dining.DeadlockStutterStep"/> edges.
    /// </summary>
    public class DiningGraphProbeTests
    {
        private static (int states, int deadlocks) Walk(StateGraphNode root)
        {
            var seen = new HashSet<StateGraphNode>();
            var stack = new Stack<StateGraphNode>();
            stack.Push(root); seen.Add(root);
            int deadlocks = 0;
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                foreach (var e in s.Edges)
                {
                    if (seen.Add(e.Target)) stack.Push(e.Target);
                    if (e.StepFunction is Dining.DeadlockStutterStep) deadlocks++;
                }
            }
            return (seen.Count, deadlocks);
        }

        [Test]
        public void Naive_Has_DeadlockState()
        {
            var root = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: false), Dining.InitialState());
            var (states, deadlocks) = Walk(root);
            TestContext.WriteLine($"naive: {states} states, {deadlocks} deadlock edges");
            Assert.Greater(deadlocks, 0, "Naive variant should expose at least one deadlock self-loop.");
        }

        [Test]
        public void Asymmetric_HasNo_DeadlockState()
        {
            var root = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: true), Dining.InitialState());
            var (states, deadlocks) = Walk(root);
            TestContext.WriteLine($"asymmetric: {states} states, {deadlocks} deadlock edges");
            Assert.AreEqual(0, deadlocks, "Asymmetric variant must be deadlock-free.");
        }
    }
}
