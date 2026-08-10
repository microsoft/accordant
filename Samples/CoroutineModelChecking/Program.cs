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
    }
}
