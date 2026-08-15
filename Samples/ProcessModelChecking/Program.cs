// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace ProcessModelChecking;

using System;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

internal static class Program
{
    private static void Main()
    {
        var root = StructuredProcessSample.Explore();
        var report = ProcessGraphDiagnostics.Describe(root);

        Console.WriteLine(
            $"{report.ConfigurationCount} exact configurations, " +
            $"{report.TransitionCount} edges, " +
            $"{report.DomainStateCount} domain states, complete={report.Complete}");

        foreach (var (role, forms) in report.ContinuationFormsByRole)
        {
            Console.WriteLine($"{role}:");
            foreach (var form in forms)
            {
                Console.WriteLine($"  {form}");
            }
        }

        if (!report.Complete ||
            report.ConfigurationCount != 6 ||
            report.TransitionCount != 7 ||
            report.DomainStateCount != 6)
        {
            throw new InvalidOperationException(
                "The documented structured-process graph measurements changed.");
        }
    }
}
