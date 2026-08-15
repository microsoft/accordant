// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;
using NUnit.Framework;

/// <summary>
/// The multi-client contract: two concurrent one-shot clients contend for the
/// capacity-one request slot, exactly one transaction is admitted at a time, the
/// slot is never overwritten, replies are persistent and correctly targeted, and
/// draining one transaction admits the next.
/// </summary>
[TestFixture]
public class WalProcessMultiClientTests
{
    private static readonly WalConfig Config = WalConfig.Default;

    private static IEnumerable<(StateGraphNode Node, StateGraphEdge Edge, ProcessTransition Transition)>
        AllEdges(StateGraphNode root)
        => ModelGraph.Reachable(root)
            .SelectMany(node => node.Edges.Select(edge =>
                (node, edge, ModelGraph.Transition(edge))));

    [Test]
    public void BothClientsContendForTheSlotAtTheStart()
    {
        var root = WriteAheadLog.Explore(Config);

        // The initial clean state admits any client: two atomic StepWhen submit
        // edges race for the single slot.
        var rootSubmits = root.Edges
            .Select(ModelGraph.Transition)
            .Where(t => t.SemanticAction is ClientAction.Submit)
            .Select(t => t.Subject.ToString())
            .ToList();
        Assert.That(rootSubmits, Is.EquivalentTo(new[] { "Alice", "Bob" }));
    }

    [Test]
    public void SubmissionNeverOverwritesThePendingSlotAndIsOneShotPerClient()
    {
        var root = WriteAheadLog.Explore(Config);

        foreach (var (node, _, t) in AllEdges(root))
        {
            if (t.SemanticAction is ClientAction.Submit)
            {
                var s = (WalProcessState)node.State;
                var client = (ClientId)t.Subject;

                // The guard is re-evaluated live, so a submission only ever lands
                // in a free slot — never overwriting another client's request.
                Assert.That(s.Exchange.Pending, Is.Null, "submit only claims a free slot");

                // A client with a persistent reply never submits again.
                Assert.That(
                    s.Exchange.ReplyOf(client), Is.EqualTo(Outcome.None),
                    "a one-shot client with a reply cannot resubmit");
            }
        }
    }

    [Test]
    public void AtMostOneTransactionIsOutstandingAndNeverConcurrent()
    {
        var root = WriteAheadLog.Explore(Config);

        // The slot has capacity one: every reachable state holds at most one
        // pending request, and an uncommitted in-flight redo (still in doubt)
        // always belongs to the one outstanding client. A committed log may
        // outlive its report until truncation, so only the in-doubt window is
        // tied to the slot.
        foreach (var node in ModelGraph.Reachable(root))
        {
            var s = (WalProcessState)node.State;
            if (s.Wal.LogRedo && !s.Wal.LogCommit)
            {
                Assert.That(
                    s.Exchange.Pending, Is.Not.Null,
                    "an uncommitted in-flight transaction always has an outstanding request");
            }
        }
    }

    [Test]
    public void EveryReportGoesToTheOutstandingClientWithTheMatchingOutcome()
    {
        var root = WriteAheadLog.Explore(Config);

        foreach (var (node, edge, t) in AllEdges(root))
        {
            if (t.SemanticAction is WalAction.AckCommit or WalAction.AckAbort)
            {
                var before = (WalProcessState)node.State;
                var after = (WalProcessState)edge.Target.State;
                var client = before.Exchange.Pending.Client;
                var expected = t.SemanticAction is WalAction.AckCommit
                    ? Outcome.Committed
                    : Outcome.Aborted;

                Assert.That(after.Exchange.ReplyOf(client), Is.EqualTo(expected),
                    "the report reaches the outstanding client with the right outcome");
                Assert.That(after.Exchange.Pending, Is.Null, "the report clears the slot");
            }
        }
    }

    [Test]
    public void EachClientCompletesExactlyOnceAsAOneShotProcess()
    {
        var root = WriteAheadLog.Explore(Config);

        var completions = AllEdges(root)
            .Where(x => x.Transition.Control == ProcessControlKind.Completion &&
                        Roles.Clients.Contains(x.Transition.ProcessRole))
            .Select(x => x.Transition.ProcessRole)
            .Distinct()
            .ToList();
        Assert.That(completions, Is.EquivalentTo(Roles.Clients), "each client completes once");

        // A state exists where both clients have completed with a persistent
        // reply: the whole workload drains.
        Assert.That(
            ModelGraph.Reachable(root).Any(n =>
                ((WalProcessState)n.State).Exchange.Replies.All(o => o != Outcome.None)),
            Is.True);
    }

    [Test]
    public void DrainingOneTransactionAdmitsTheNextClient()
    {
        var root = WriteAheadLog.Explore(Config);

        // Truncation always cleans a committed, fully-installed log.
        var truncateEdges = AllEdges(root)
            .Where(x => x.Transition.SemanticAction is WalAction.TruncateLog)
            .ToList();
        Assert.That(truncateEdges, Is.Not.Empty);
        foreach (var (node, edge, _) in truncateEdges)
        {
            var before = (WalProcessState)node.State;
            var after = (WalProcessState)edge.Target.State;
            Assert.That(before.Wal.IsClean, Is.False);
            Assert.That(after.Wal.IsClean, Is.True, "truncation cleans the log for the next transaction");
        }

        // A second client is admitted only after an earlier transaction reported:
        // a submit taken from a state where some client already has a reply.
        var secondAdmission = AllEdges(root).Any(x =>
            x.Transition.SemanticAction is ClientAction.Submit &&
            ((WalProcessState)x.Node.State).Exchange.Replies.Count(o => o != Outcome.None) >= 1);
        Assert.That(secondAdmission, Is.True, "the next client is admitted after the previous drains");
    }

    [Test]
    public void BothClientAdmissionOrdersAreReachable()
    {
        var root = WriteAheadLog.Explore(Config);
        var orders = ReachableAdmissionOrders(root);

        var expected = new[] { "Alice>Bob", "Bob>Alice" };
        foreach (var order in expected)
        {
            Assert.That(orders, Contains.Item(order), $"admission order {order} should be reachable");
        }
        Assert.That(orders, Is.EquivalentTo(expected), "only the two orders are reachable");
    }

    /// <summary>Every full two-client admission order reachable from the root.</summary>
    private static HashSet<string> ReachableAdmissionOrders(StateGraphNode root)
    {
        var full = new HashSet<string>();
        var seen = new HashSet<string>();
        var stack = new Stack<(StateGraphNode Node, string Order, int Count)>();
        stack.Push((root, string.Empty, 0));

        while (stack.Count > 0)
        {
            var (node, order, count) = stack.Pop();
            if (!seen.Add(node.GetNodeFingerprint() + "|" + order))
            {
                continue;
            }

            if (count == Roles.Clients.Count)
            {
                full.Add(order);
                continue;
            }

            foreach (var edge in node.Edges)
            {
                var transition = ModelGraph.Transition(edge);
                if (transition.SemanticAction is ClientAction.Submit)
                {
                    var name = transition.Subject.ToString();
                    var next = order.Length == 0 ? name : order + ">" + name;
                    stack.Push((edge.Target, next, count + 1));
                }
                else
                {
                    stack.Push((edge.Target, order, count));
                }
            }
        }

        return full;
    }
}
