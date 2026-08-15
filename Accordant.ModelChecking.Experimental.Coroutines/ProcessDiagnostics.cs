// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Accordant;

/// <summary>A compact structural report for an explored process graph.</summary>
public sealed class ProcessGraphReport
{
    internal ProcessGraphReport(
        int domainStateCount,
        int configurationCount,
        int transitionCount,
        bool complete,
        IReadOnlyDictionary<string, IReadOnlyList<string>> continuationFormsByRole)
    {
        DomainStateCount = domainStateCount;
        ConfigurationCount = configurationCount;
        TransitionCount = transitionCount;
        Complete = complete;
        ContinuationFormsByRole = continuationFormsByRole;
    }

    /// <summary>The number of distinct domain-state representations.</summary>
    public int DomainStateCount { get; }

    /// <summary>
    /// The exact number of graph configurations, including continuation and
    /// failure-domain control state.
    /// </summary>
    public int ConfigurationCount { get; }

    /// <summary>The number of explicit graph edges.</summary>
    public int TransitionCount { get; }

    /// <summary>Whether no reachable node was cut off by a depth bound.</summary>
    public bool Complete { get; }

    /// <summary>Distinct human-readable continuation forms observed per role.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ContinuationFormsByRole { get; }
}

/// <summary>Diagnostics for ordinary graphs produced by ProcessSystemModel.</summary>
public static class ProcessGraphDiagnostics
{
    /// <summary>
    /// Walks the reachable graph once and reports domain-state/configuration
    /// counts plus the structured continuation forms observed for each role.
    /// </summary>
    public static ProcessGraphReport Describe(StateGraphNode root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var nodes = Reachable(root);
        var forms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var rootDiagnostics = root.StepFunctions
            .OfType<IProcessSchedulerDiagnostics>()
            .SingleOrDefault();
        if (rootDiagnostics != null)
        {
            foreach (var role in rootDiagnostics.ProcessRoles)
            {
                forms[role] = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var role in rootDiagnostics.RepeatedActionRoles)
            {
                forms[role] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "<stateless recurring action>"
                };
            }
        }

        foreach (var node in nodes)
        {
            var scheduler = node.StepFunctions.OfType<IProcessSchedulerStep>().SingleOrDefault();
            if (scheduler == null)
            {
                continue;
            }

            foreach (var process in scheduler.LiveProcesses)
            {
                if (!forms.TryGetValue(process.Role, out var roleForms))
                {
                    roleForms = new HashSet<string>(StringComparer.Ordinal);
                    forms[process.Role] = roleForms;
                }

                roleForms.Add(process.ContinuationDescription);
            }
        }

        var frozenForms = forms.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)new ReadOnlyCollection<string>(
                (item.Value.Count == 0
                    ? new List<string> { "<not live in explored graph>" }
                    : item.Value.OrderBy(value => value, StringComparer.Ordinal).ToList())),
            StringComparer.Ordinal);

        return new ProcessGraphReport(
            nodes.Select(node => node.State.StringRepresentation()).Distinct().Count(),
            nodes.Count,
            nodes.Sum(node => node.Edges.Count),
            nodes.All(node => !node.IsDepthFrontier),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(frozenForms));
    }

    private static IReadOnlyList<StateGraphNode> Reachable(StateGraphNode root)
    {
        var nodes = new List<StateGraphNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<StateGraphNode>();
        pending.Push(root);

        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node.GetNodeFingerprint()))
            {
                continue;
            }

            nodes.Add(node);
            foreach (var edge in node.Edges)
            {
                pending.Push(edge.Target);
            }
        }

        return nodes;
    }
}

internal interface IProcessSchedulerDiagnostics
{
    IReadOnlyList<string> ProcessRoles { get; }
    IReadOnlyList<string> RepeatedActionRoles { get; }
}
