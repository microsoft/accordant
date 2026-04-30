// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Manual testing example using spec.Allows() for targeted scenario validation.
/// Use this pattern when you want to test specific scenarios explicitly.
/// </summary>
public class ManualTestExample
{
    [Test]
    public async Task ManualTest_StackScenario()
    {
        // 1. Create the spec and initial state
        var spec = new StackSpec();
        var initialState = new StackState<int>();
        
        // 2. Create testing context and register the real system
        var context = spec.CreateTestingContext();
        context.Register(new Stack<int>());

        // 3. Create StateProfile to track possible states
        var stateProfile = new StateProfile(initialState);

        // Helper to execute and validate each step
        async Task<TResp> Allows<TReq, TResp>(
            Operation<TReq, TResp, StackState<int>> op, 
            TReq request)
        {
            // Execute against real system
            var response = await op.ExecuteAsync(context, request);
            
            // Validate response against spec
            var (isValid, message, nextProfile) = spec.Allows(
                op, request, response, stateProfile);
            
            Assert.IsTrue(isValid, message);
            
            // Update state profile for next operation
            stateProfile = nextProfile;
            return response;
        }

        // 4. Execute scenario steps - each validated against the spec
        await Allows(spec.Push, 1);    // Push 1
        await Allows(spec.Push, 2);    // Push 2
        await Allows(spec.Push, 3);    // Push 3

        var count = await Allows(spec.Count, Unit.Value);
        Assert.AreEqual(3, count);

        var peeked = await Allows(spec.Peek, Unit.Value);
        Assert.AreEqual(3, peeked);  // Top is 3

        var popped = await Allows(spec.Pop, Unit.Value);
        Assert.AreEqual(3, popped);  // Pop returns 3

        var isEmpty = await Allows(spec.IsEmpty, Unit.Value);
        Assert.IsFalse(isEmpty);  // Still has 2 items
    }
}
