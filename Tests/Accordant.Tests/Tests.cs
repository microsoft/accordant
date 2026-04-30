// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using System.Collections.Generic;
    using Microsoft.Accordant;
    using NUnit.Framework;

    [TestFixture]
    public class StateTests
    {
        [Test]
        public void StringDictState_CloneTests()
        {
            var dictState = new StringDictState();
            dictState.Map["k"] = "v";

            var clonedDictState = (StringDictState)dictState.Clone();
            Assert.That(dictState.GetStateHash(), Is.EqualTo(clonedDictState.GetStateHash()));

            clonedDictState.Map["k2"] = "v2";

            Assert.That(dictState.GetStateHash(), Is.Not.EqualTo(clonedDictState.GetStateHash()));
            Assert.That(dictState.Map.Count, Is.EqualTo(1));
            Assert.That(clonedDictState.Map.Count, Is.EqualTo(2));
        }

        [Test]
        public void NestedDictState_CloneTests()
        {
            var dictState = new NestedDictState();
            dictState.Map["k"] = new StringValueState { Value = "v" };

            var clonedDictState = (NestedDictState)dictState.Clone();
            Assert.That(dictState.GetStateHash(), Is.EqualTo(clonedDictState.GetStateHash()));

            clonedDictState.Map["k2"] = new StringValueState { Value = "v2" };

            Assert.That(dictState.GetStateHash(), Is.Not.EqualTo(clonedDictState.GetStateHash()));
            Assert.That(dictState.Map.Count, Is.EqualTo(1));
            Assert.That(clonedDictState.Map.Count, Is.EqualTo(2));
        }

        [Test]
        public void StringListState_CloneTests()
        {
            var listState = new StringListState();
            listState.Items.Add("v");

            var clonedListState = (StringListState)listState.Clone();
            Assert.That(listState.GetStateHash(), Is.EqualTo(clonedListState.GetStateHash()));

            clonedListState.Items.Add("v2");

            Assert.That(listState.GetStateHash(), Is.Not.EqualTo(clonedListState.GetStateHash()));
            Assert.That(listState.Items.Count, Is.EqualTo(1));
            Assert.That(clonedListState.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void NestedListState_CloneTests()
        {
            var listState = new NestedListState();
            listState.Items.Add(new StringValueState { Value = "v" });

            var clonedListState = (NestedListState)listState.Clone();
            Assert.That(listState.GetStateHash(), Is.EqualTo(clonedListState.GetStateHash()));

            clonedListState.Items.Add(new StringValueState { Value = "v2" });

            Assert.That(listState.GetStateHash(), Is.Not.EqualTo(clonedListState.GetStateHash()));
            Assert.That(listState.Items.Count, Is.EqualTo(1));
            Assert.That(clonedListState.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void StringValueState_FreezeTests()
        {
            var state = new StringValueState { Value = "test" };
            Assert.That(state.IsFrozen, Is.False);

            state.Freeze();
            Assert.That(state.IsFrozen, Is.True);

            // Verify hash is computed after freeze
            var hash1 = state.GetStateHash();
            var hash2 = state.GetStateHash();
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void IntValueState_HashDiffers()
        {
            var state1 = new IntValueState { Value = 1 };
            var state2 = new IntValueState { Value = 2 };
            var state3 = new IntValueState { Value = 1 };

            state1.Freeze();
            state2.Freeze();
            state3.Freeze();

            Assert.That(state1.GetStateHash(), Is.Not.EqualTo(state2.GetStateHash()));
            Assert.That(state1.GetStateHash(), Is.EqualTo(state3.GetStateHash()));
        }

        [Test]
        public void NestedState_DeepClone()
        {
            var outer = new NestedListState();
            outer.Items.Add(new StringValueState { Value = "original" });

            var cloned = (NestedListState)outer.Clone();

            // Modify cloned nested state
            cloned.Items[0].Value = "modified";

            // Original should be unchanged (deep clone)
            Assert.That(outer.Items[0].Value, Is.EqualTo("original"));
            Assert.That(cloned.Items[0].Value, Is.EqualTo("modified"));
        }

        [Test]
        public void NestedDictState_DeepClone()
        {
            var outer = new NestedDictState();
            outer.Map["key"] = new StringValueState { Value = "original" };

            var cloned = (NestedDictState)outer.Clone();

            // Modify cloned nested state
            cloned.Map["key"].Value = "modified";

            // Original should be unchanged (deep clone)
            Assert.That(outer.Map["key"].Value, Is.EqualTo("original"));
            Assert.That(cloned.Map["key"].Value, Is.EqualTo("modified"));
        }
    }
}
