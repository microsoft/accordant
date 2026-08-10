// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CoroutineModelChecking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// Executable evidence for what the replay runtime rejects and what it cannot
/// see. Every method here reports an outcome instead of asserting one, so the
/// documented soundness boundary can be read from the sample output.
/// </summary>
public static class SafetyHardeningCaseStudy
{
    private static int impureReads;
    private static int foreignAwaitResults;

    /// <summary>
    /// A Read selector that depends on a counter instead of the frozen state is
    /// rejected because the selector is evaluated twice on the same state.
    /// </summary>
    public static string RejectedImpureSelector()
    {
        impureReads = 0;
        return Rejection(() => CoroutineModel.Explore(
            "impure-selector",
            new LoopToggleState(),
            ImpureSelectorWorkflow,
            verifyDeterminism: true));
    }

    /// <summary>
    /// A foreign await that completes synchronously is invisible to the async
    /// method builder, so it is only caught when it changes which checkpoints
    /// the workflow reaches.
    /// </summary>
    public static string RejectedNondeterministicForeignAwait()
    {
        foreignAwaitResults = 0;
        return Rejection(() => CoroutineModel.Explore(
            "varying-foreign-await",
            new LoopToggleState(),
            VaryingForeignAwaitWorkflow,
            verifyDeterminism: true));
    }

    /// <summary>
    /// Reports whether a synchronously completed foreign await compiles without
    /// any diagnostic. It does: this is the documented runtime limitation.
    /// </summary>
    public static bool SynchronouslyCompletedForeignAwaitIsAccepted()
    {
        var root = CoroutineModel.Explore(
            "completed-foreign-await",
            new LoopToggleState(),
            CompletedForeignAwaitWorkflow);
        return root.Edges.Count == 1;
    }

    /// <summary>Reports how closely each captured external input can be monitored.</summary>
    public static IReadOnlyList<string> CapturedInputReport()
    {
        var budget = 2;
        var seen = new List<string>();

        async ModelTask CapturingWorkflow(ModelContext<LoopToggleState> context)
        {
            await context.Step("toggle", state => state.On = budget + seen.Count > 0);
        }

        return CoroutineModel
            .DescribeCapturedInputs<LoopToggleState>(CapturingWorkflow)
            .Select(input => input.ToString())
            .ToList();
    }

    private static async ModelTask ImpureSelectorWorkflow(ModelContext<LoopToggleState> context)
    {
        var observed = await context.Read("clock-like", state => state.On || impureReads++ > 0);
        await context.Step("toggle", state => state.On = !observed);
    }

    private static async ModelTask VaryingForeignAwaitWorkflow(ModelContext<LoopToggleState> context)
    {
        var even = await Task.FromResult(foreignAwaitResults++ % 2 == 0);
        if (even)
        {
            await context.Step("even", state => state.On = true);
        }
        else
        {
            await context.Step("odd", state => state.On = false);
        }
    }

    private static async ModelTask CompletedForeignAwaitWorkflow(ModelContext<LoopToggleState> context)
    {
        await Task.CompletedTask;
        await context.Step("toggle", state => state.On = !state.On);
    }

    private static string Rejection(Func<StateGraphNode> explore)
    {
        try
        {
            explore();
        }
        catch (ModelDefinitionException exception)
        {
            return FirstSentence(exception.Message);
        }

        return "accepted (no diagnostic)";
    }

    private static string FirstSentence(string message)
    {
        var stop = message.IndexOf(". ", StringComparison.Ordinal);
        return stop < 0 ? message : message.Substring(0, stop + 1);
    }
}
