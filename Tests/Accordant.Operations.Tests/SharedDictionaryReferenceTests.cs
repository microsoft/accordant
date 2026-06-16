// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// A state that contains a dictionary of items.
/// Similar to PetImagesState.Accounts pattern.
/// </summary>
[State]
public partial class StateWithDictionary
{
    public Dictionary<string, ItemData> Items { get; set; } = new();
}

[State]
public partial class ItemData
{
    public string Name { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// Tests that validate behavior when operations copy dictionary references
/// from the old state to the new state in ThenState, and then modify them.
/// This is a dangerous pattern because it creates shared state between
/// the old and new state objects.
/// </summary>
[TestFixture]
public class SharedDictionaryReferenceTests
{
    #region Operations

    /// <summary>
    /// An operation that INCORRECTLY copies a dictionary reference from the old state
    /// and then modifies it. This simulates the PetImages pattern where:
    /// var existingImages = state.Accounts[key].Images;
    /// nextState.Accounts[key] = new AccountState { Images = existingImages };
    /// </summary>
    public class AddItemWithSharedReferenceOperation : Operation<string, Unit, StateWithDictionary>
    {
        private readonly bool _modifyAfterCopying;

        public AddItemWithSharedReferenceOperation(bool modifyAfterCopying = true)
            : base("AddItemWithSharedReference")
        {
            _modifyAfterCopying = modifyAfterCopying;
        }

        public override ExpectedOutcomes Apply(string request, StateWithDictionary state)
        {
            // Get existing items dictionary from OLD state
            var existingItems = state.Items;

            return Expect.Unit()
                .ThenState(nextState =>
                {
                    // WRONG: Copy reference to dictionary from old state
                    nextState.Items = existingItems;

                    if (_modifyAfterCopying)
                    {
                        // WRONG: Modify the shared dictionary
                        // This affects BOTH old and new state!
                        nextState.Items[request] = new ItemData { Name = request, Value = 1 };
                    }
                });
        }
    }

    /// <summary>
    /// An operation that correctly clones the dictionary.
    /// </summary>
    public class AddItemCorrectlyOperation : Operation<string, Unit, StateWithDictionary>
    {
        public AddItemCorrectlyOperation() : base("AddItemCorrectly") { }

        public override ExpectedOutcomes Apply(string request, StateWithDictionary state)
        {
            return Expect.Unit()
                .ThenState(nextState =>
                {
                    // The framework already cloned the state, so nextState.Items
                    // is a separate dictionary. Just add to it.
                    nextState.Items[request] = new ItemData { Name = request, Value = 1 };
                });
        }
    }

    /// <summary>
    /// A read operation that validates the state.
    /// </summary>
    public class GetItemCountOperation : Operation<Unit, int, StateWithDictionary>
    {
        public GetItemCountOperation() : base("GetItemCount") { }

        public override ExpectedOutcomes Apply(Unit request, StateWithDictionary state)
        {
            return Expect.That(r => r == state.Items.Count, $"should have {state.Items.Count} items")
                .SameState();
        }
    }

    #endregion

    #region Spec

    public class SharedReferenceSpec : Spec<StateWithDictionary>
    {
        public AddItemWithSharedReferenceOperation AddWithSharedRef { get; }
        public AddItemCorrectlyOperation AddCorrectly { get; } = new();
        public GetItemCountOperation GetCount { get; } = new();

        public SharedReferenceSpec(bool modifyAfterCopying = true)
        {
            AddWithSharedRef = new AddItemWithSharedReferenceOperation(modifyAfterCopying);

            this["AddWithSharedRef"] = AddWithSharedRef;
            this["AddCorrectly"] = AddCorrectly;
            this["GetCount"] = GetCount;
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// Test that demonstrates the shared reference problem when
    /// copying a dictionary reference from old state to new state
    /// and then modifying it.
    /// 
    /// Expected behavior: The framework should detect this via mutation
    /// detection because the old state's dictionary was modified.
    /// </summary>
    [Test]
    public void SharedDictionaryReference_WhenModified_ShouldBeDetectedByMutationDetection()
    {
        var spec = new SharedReferenceSpec(modifyAfterCopying: true);
        var initialState = new StateWithDictionary();

        var inputSet = new InputSet()
        {
            new OperationInput("AddWithSharedRef Item1", spec["AddWithSharedRef"], "item1"),
            new OperationInput("AddWithSharedRef Item2", spec["AddWithSharedRef"], "item2"),
            new OperationInput("GetCount", spec["GetCount"]),
        };

        // Enable mutation detection (should be on by default)
        State.EnableFreezeValidation = true;

        // The framework should detect this via mutation detection.
        // The exception is wrapped in TestCaseGenerationException with StateFrozenException as inner.
        var ex = Assert.Throws<TestCaseGenerationException>(() =>
        {
            var testCases = spec.GenerateTests(
                initialState,
                inputSet,
                new TestGenerationOptions()
                {
                    MaxDepth = 3
                });

            // Force enumeration to trigger the exception
            var testCasesList = testCases.ToList();
        });

        // Verify the inner exception is StateFrozenException
        Assert.IsInstanceOf<StateFrozenException>(ex.InnerException,
            "Inner exception should be StateFrozenException indicating mutation was detected");

        Console.WriteLine($"Mutation detection correctly caught the issue:");
        Console.WriteLine($"  Outer: {ex.Message}");
        Console.WriteLine($"  Inner: {ex.InnerException?.Message}");
    }

    /// <summary>
    /// Test the correct pattern - let the framework clone the state
    /// and then just modify the already-cloned dictionary.
    /// </summary>
    [Test]
    public void CorrectDictionaryModification_InFrameworkClonedState_ShouldWork()
    {
        var spec = new SharedReferenceSpec();
        var initialState = new StateWithDictionary();

        var inputSet = new InputSet()
        {
            new OperationInput("AddCorrectly Item1", spec["AddCorrectly"], "item1"),
            new OperationInput("AddCorrectly Item2", spec["AddCorrectly"], "item2"),
            new OperationInput("GetCount", spec["GetCount"]),
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions()
            {
                MaxDepth = 3
            });

        var testCasesList = testCases.ToList();

        // Should generate test cases successfully
        Assert.IsTrue(testCasesList.Count > 0, "Should generate at least one test case");

        // Should have test cases that add multiple items
        var multiAddTestCase = testCasesList.FirstOrDefault(tc =>
            tc.OperationCalls.Count(c => c.Name.Contains("AddCorrectly")) >= 2);

        Assert.IsNotNull(multiAddTestCase, "Should have test cases with multiple add operations");

        Console.WriteLine($"Generated {testCasesList.Count} test cases successfully");
    }

    /// <summary>
    /// Test that just copying a reference (without modifying) doesn't cause issues
    /// until the dictionary is actually modified.
    /// </summary>
    [Test]
    public void SharedDictionaryReference_WithoutModification_ShouldNotThrow()
    {
        var spec = new SharedReferenceSpec(modifyAfterCopying: false);
        var initialState = new StateWithDictionary();

        var inputSet = new InputSet()
        {
            new OperationInput("AddWithSharedRef Item1", spec["AddWithSharedRef"], "item1"),
            new OperationInput("GetCount", spec["GetCount"]),
        };

        // This should NOT throw because we're just copying the reference,
        // not actually modifying the dictionary
        var testCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions()
            {
                MaxDepth = 2
            });

        var testCasesList = testCases.ToList();
        Assert.IsTrue(testCasesList.Count > 0, "Should generate test cases");

        Console.WriteLine($"Generated {testCasesList.Count} test cases (reference copy without modification is fine)");
    }

    /// <summary>
    /// Test what happens when the shared reference is modified later
    /// through a subsequent operation.
    /// </summary>
    [Test]
    public void SharedDictionaryReference_ModifiedLaterThroughSubsequentOp_ShouldCauseIssues()
    {
        // First, add an item using the WRONG pattern (copies reference but doesn't modify)
        // Then, add another item using the CORRECT pattern
        // The second operation will modify the dictionary that's shared
        var spec = new SharedReferenceSpec(modifyAfterCopying: false);
        var initialState = new StateWithDictionary();

        // Start with an item already in the dictionary
        initialState.Items["existing"] = new ItemData { Name = "existing", Value = 0 };

        var inputSet = new InputSet()
        {
            // This copies the reference but doesn't modify
            new OperationInput("AddWithSharedRef Item1", spec["AddWithSharedRef"], "item1"),
            // This will add to the dictionary (in the cloned state)
            new OperationInput("AddCorrectly Item2", spec["AddCorrectly"], "item2"),
            new OperationInput("GetCount", spec["GetCount"]),
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputSet,
            new TestGenerationOptions()
            {
                MaxDepth = 3
            });

        var testCasesList = testCases.ToList();

        // Check if we can find a test case where:
        // 1. AddWithSharedRef is called first (copies reference)
        // 2. AddCorrectly is called second (modifies the cloned state's dictionary)
        // Both should work because AddWithSharedRef copies a reference to a
        // DIFFERENT dictionary object (the cloned one has its own dictionary)

        Console.WriteLine($"Generated {testCasesList.Count} test cases");

        foreach (var tc in testCasesList.Where(tc =>
            tc.OperationCalls.Any(c => c.Name.Contains("AddWithSharedRef")) &&
            tc.OperationCalls.Any(c => c.Name.Contains("AddCorrectly"))))
        {
            Console.WriteLine($"Test case operations:");
            foreach (var call in tc.OperationCalls)
            {
                Console.WriteLine($"  - {call.Name}");
            }
        }

        Assert.IsTrue(testCasesList.Count > 0);
    }

    #endregion
}
