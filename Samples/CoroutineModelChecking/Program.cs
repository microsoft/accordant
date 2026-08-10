// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CoroutineModelChecking;

internal static class Program
{
    private static void Main()
    {
        var manual = WorkerCompetitionCaseStudy.BuildManualGraph();
        var coroutine = WorkerCompetitionCaseStudy.BuildCoroutineGraph();
        var manualSize = WorkerCompetitionCaseStudy.GetRawGraphSize(manual);
        var coroutineSize = WorkerCompetitionCaseStudy.GetRawGraphSize(coroutine);

        Console.WriteLine($"manual: {manualSize.Nodes} nodes, {manualSize.Edges} edges");
        Console.WriteLine($"coroutine: {coroutineSize.Nodes} nodes, {coroutineSize.Edges} edges");
        Console.WriteLine(
            $"projected states: {WorkerCompetitionCaseStudy.ProjectedDomainStates(coroutine).Count}");
        Console.WriteLine(
            $"projected changing transitions: " +
            $"{WorkerCompetitionCaseStudy.CoroutineChangingTransitionsHidingChoose(coroutine).Count}");

        var naive = LoopReplayCaseStudy.MeasureNaive();
        var rebased = LoopReplayCaseStudy.MeasureRebased();
        Console.WriteLine(
            $"naive loop (bound 4): {naive.Nodes} nodes, {naive.Edges} edges, " +
            $"{naive.DistinctContinuationIdentities} continuation identities, " +
            $"tape length {naive.LongestVisibleReplayTape}, frontier={naive.HasDepthFrontier}, " +
            $"cycle={naive.HasGraphCycle}");
        Console.WriteLine(
            $"Loop-rebased loop: {rebased.Nodes} nodes, {rebased.Edges} edges, " +
            $"{rebased.DistinctContinuationIdentities} continuation identities, " +
            $"tape length {rebased.LongestVisibleReplayTape}, frontier={rebased.HasDepthFrontier}, " +
            $"cycle={rebased.HasGraphCycle}");

        Console.WriteLine($"impure selector: {SafetyHardeningCaseStudy.RejectedImpureSelector()}");
        Console.WriteLine(
            $"varying foreign await: {SafetyHardeningCaseStudy.RejectedNondeterministicForeignAwait()}");
        Console.WriteLine(
            "synchronously completed foreign await accepted (runtime limitation): " +
            $"{SafetyHardeningCaseStudy.SynchronouslyCompletedForeignAwaitIsAccepted()}");
        foreach (var captured in SafetyHardeningCaseStudy.CapturedInputReport())
        {
            Console.WriteLine($"captured input: {captured}");
        }
    }
}
