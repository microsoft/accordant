// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests;

using System.Collections.Generic;
using Microsoft.Accordant;
using NUnit.Framework;

[TestFixture]
public class StateFreezeValidationTests
{
    private sealed class MutableListState : State
    {
        public List<string> Items { get; } = new List<string>();

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clone = new MutableListState();
            clone.Items.AddRange(Items);
            clonedMap[this] = clone;
        }

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
        {
            return string.Join(",", Items);
        }

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    [Test]
    public void Freeze_RejectsMutationAfterTheInitialFreeze()
    {
        var state = new MutableListState();
        var previousValidationSetting = state.EnableFreezeValidation;
        try
        {
            state.EnableFreezeValidation = true;
            state.Items.Add("before-freeze");
            state.Freeze();

            state.Items.Add("after-freeze");

            var exception = Assert.Throws<StateFrozenException>(() => state.Freeze());

            Assert.That(exception.Message, Does.Contain("mutated after freezing"));
        }
        finally
        {
            state.EnableFreezeValidation = previousValidationSetting;
        }
    }

    [Test]
    public void FreezeValidation_IsConfiguredPerStateInstance()
    {
        var validationDisabledState = new MutableListState
        {
            EnableFreezeValidation = false
        };
        var validationEnabledState = new MutableListState();

        validationDisabledState.Freeze();
        validationEnabledState.Freeze();
        validationDisabledState.Items.Add("mutation");
        validationEnabledState.Items.Add("mutation");

        Assert.DoesNotThrow(() => validationDisabledState.Freeze());
        Assert.Throws<StateFrozenException>(() => validationEnabledState.Freeze());
        Assert.That(validationDisabledState.Clone().EnableFreezeValidation, Is.False);
    }
}
