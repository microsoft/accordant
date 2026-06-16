// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

using System.Linq;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public class TestGeneratorTests
{
    [Test]
    public void ShouldUnwindStepFunctionTests()
    {
        var spec = new SimpleAsyncClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("TriggerAsyncFirstStage", spec["TriggerAsyncFirstStage"]),
            new OperationInput("TriggerSyncSecondStage", spec["TriggerSyncSecondStage"]),
            new OperationInput("TriggerSyncThirdStage", spec["TriggerSyncThirdStage"]),
            new OperationInput("GetStage", spec["GetStage"]),
        };

        foreach (var shouldUnwindAllStepFunctions in new bool[] { true, false })
        {
            var testCases = spec.GenerateTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    UnwindAllTerminatingStepFunctions = shouldUnwindAllStepFunctions
                });

            bool singleTestCaseContainsSecondAndThirdStage =
                testCases.Any(tc =>
                {
                    var calls = tc.OperationCalls;

                    bool secondStage = false;
                    bool thirdStage = false;

                    foreach (var call in calls)
                    {
                        if (call.Name.Contains("SecondStage") &&
                            !secondStage)
                        {
                            secondStage = true;
                        }

                        if (secondStage &&
                            call.Name.Contains("ThirdStage") &&
                            !thirdStage)
                        {
                            thirdStage = true;
                        }
                    }

                    return secondStage && thirdStage;
                });

            Assert.IsTrue(shouldUnwindAllStepFunctions ?
                singleTestCaseContainsSecondAndThirdStage :
                !singleTestCaseContainsSecondAndThirdStage);
        }
    }

    [Test]
    public void MaxApplicationCountTest()
    {
        var spec = new SimpleStatefulClassSpec();
        var initialState = new CounterState(0);

        var inputSet = new InputSet()
        {
            new OperationInput("Add 1", spec["Add"], 1),
            new OperationInput("Count", spec["Count"])
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions()
            {
                MaxDepth = 5
            });

        Assert.True(testCases.Any(tc =>
            tc.OperationCalls.Count(c => c.Name.Contains("Add 1")) > 2));

        var trimmedTestCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions()
            {
                MaxOperationApplicationCount = 2
            });

        Assert.False(trimmedTestCases.Any(tc =>
            tc.OperationCalls.Count(c => c.Name.Contains("Add 1")) > 2));

        Assert.True(trimmedTestCases.Any(tc =>
            tc.OperationCalls.Count(c => c.Name.Contains("Add 1")) == 2));
    }
}
