// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    #region Test State Classes

    /// <summary>
    /// Simple state with primitive types.
    /// </summary>
    public class SimpleJsonState : JsonState
    {
        public int Count { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// State with nested JsonState.
    /// </summary>
    public class ParentJsonState : JsonState
    {
        public string ParentName { get; set; }
        public ChildJsonState Child { get; set; }
    }

    public class ChildJsonState : JsonState
    {
        public string ChildName { get; set; }
        public int Value { get; set; }
    }

    /// <summary>
    /// State with cyclic reference.
    /// </summary>
    public class CyclicJsonState : JsonState
    {
        public string Name { get; set; }
        public CyclicJsonState Reference { get; set; }
    }

    /// <summary>
    /// State with collections.
    /// </summary>
    public class CollectionJsonState : JsonState
    {
        public List<int> Numbers { get; set; }
        public List<string> Names { get; set; }
        public Dictionary<string, int> Scores { get; set; }
    }

    /// <summary>
    /// State with collections containing other JsonState objects.
    /// </summary>
    public class NestedCollectionJsonState : JsonState
    {
        public List<ChildJsonState> Children { get; set; }
        public Dictionary<string, ChildJsonState> ChildrenByName { get; set; }
    }

    /// <summary>
    /// State with a computed property (getter only).
    /// </summary>
    public class ComputedPropertyJsonState : JsonState
    {
        public int Value { get; set; }
        public int DoubledValue => Value * 2;  // Computed, getter only
    }

    /// <summary>
    /// State with various numeric types and edge values.
    /// </summary>
    public class NumericEdgeCasesState : JsonState
    {
        public int IntValue { get; set; }
        public long LongValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
    }

    /// <summary>
    /// State for testing three-way cycles (A -> B -> C -> A).
    /// </summary>
    public class ThreeWayCycleState : JsonState
    {
        public string Name { get; set; }
        public ThreeWayCycleState Next { get; set; }
    }

    /// <summary>
    /// State with deeply nested structure (3+ levels).
    /// </summary>
    public class DeeplyNestedState : JsonState
    {
        public string Name { get; set; }
        public DeeplyNestedState Child { get; set; }
    }

    /// <summary>
    /// State with dictionary using int keys.
    /// </summary>
    public class IntKeyDictionaryState : JsonState
    {
        public Dictionary<int, string> ItemsById { get; set; }
    }

    /// <summary>
    /// State with nested collections (List of Lists, Dict of Dicts).
    /// </summary>
    public class NestedCollectionsState : JsonState
    {
        public List<List<int>> Matrix { get; set; }
        public Dictionary<string, Dictionary<string, int>> NestedMaps { get; set; }
        public Dictionary<string, List<int>> MapOfLists { get; set; }
    }

    /// <summary>
    /// State with DateTime and Guid properties.
    /// </summary>
    public class SpecialTypesState : JsonState
    {
        public DateTime Timestamp { get; set; }
        public Guid Id { get; set; }
    }

    /// <summary>
    /// State with atomic byte array and fingerprint property.
    /// </summary>
    public class ImageState : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic(nameof(ContentFingerprint))]
        public byte[] Content { get; set; }

        /// <summary>
        /// Fingerprint property - returns hash of content for fingerprinting.
        /// </summary>
        public string ContentFingerprint => Content == null 
            ? null 
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Content));
    }

    /// <summary>
    /// State with nested ImageState to test recursive atomic handling.
    /// </summary>
    public class AlbumState : JsonState
    {
        public string AlbumName { get; set; }
        public List<ImageState> Images { get; set; }
    }

    /// <summary>
    /// State with atomic property that has simple string fingerprint.
    /// </summary>
    public class DocumentState : JsonState
    {
        public string Title { get; set; }

        [JsonAtomic(nameof(BodyFingerprint))]
        public string Body { get; set; }

        public string BodyFingerprint => Body == null ? null : $"len:{Body.Length}";
    }

    /// <summary>
    /// State with dictionary containing ImageState values (atomic in dict values).
    /// </summary>
    public class GalleryState : JsonState
    {
        public string GalleryName { get; set; }
        public Dictionary<string, ImageState> ImagesByName { get; set; }
    }

    /// <summary>
    /// State with multiple atomic properties.
    /// </summary>
    public class MultiAtomicState : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic(nameof(ContentFingerprint))]
        public byte[] Content { get; set; }

        [JsonAtomic(nameof(MetadataFingerprint))]
        public byte[] Metadata { get; set; }

        [JsonAtomic(nameof(ThumbnailFingerprint))]
        public byte[] Thumbnail { get; set; }

        public string ContentFingerprint => Content == null
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Content));

        public string MetadataFingerprint => Metadata == null
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Metadata));

        public string ThumbnailFingerprint => Thumbnail == null
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Thumbnail));
    }

    /// <summary>
    /// State with atomic property referencing non-existent fingerprint property.
    /// </summary>
    public class InvalidFingerprintRefState : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic("NonExistentProperty")]
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// State with fingerprint property that throws exception.
    /// </summary>
    public class ThrowingFingerprintState : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic(nameof(ContentFingerprint))]
        public byte[] Content { get; set; }

        public string ContentFingerprint => 
            throw new InvalidOperationException("Fingerprint computation failed");
    }

    /// <summary>
    /// State with fingerprint property that can return null or empty.
    /// </summary>
    public class NullableFingerprint​State : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic(nameof(DataFingerprint))]
        public byte[] Data { get; set; }

        public bool ReturnEmptyInsteadOfNull { get; set; }

        public string DataFingerprint => Data == null
            ? (ReturnEmptyInsteadOfNull ? "" : null)
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Data));
    }

    /// <summary>
    /// State with cyclic reference and atomic property.
    /// </summary>
    public class CyclicAtomicState : JsonState
    {
        public string Name { get; set; }

        [JsonAtomic(nameof(ContentFingerprint))]
        public byte[] Content { get; set; }

        public CyclicAtomicState Reference { get; set; }

        public string ContentFingerprint => Content == null
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Content));
    }

    /// <summary>
    /// State for deeply nested atomic property testing (level 1).
    /// </summary>
    public class LibraryState : JsonState
    {
        public string LibraryName { get; set; }
        public List<AlbumState> Albums { get; set; }
    }

    /// <summary>
    /// Custom key type that is NOT supported as a dictionary key in JsonState.
    /// </summary>
    public class CustomKey
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// State with an unsupported dictionary key type.
    /// Used to test that UnsupportedDictionaryKeyTypeException is thrown.
    /// </summary>
    public class UnsupportedKeyState : JsonState
    {
        public Dictionary<CustomKey, string> Items { get; set; }
    }

    #endregion

    [TestFixture]
    public class JsonStateTests
    {
        [SetUp]
        public void SetUp()
        {
            // Ensure mutation detection is enabled for tests
            JsonState.EnableMutationDetection = true;
        }

        #region Basic Clone Tests

        [Test]
        public void Clone_SimpleState_CreatesIndependentCopy()
        {
            var original = new SimpleJsonState
            {
                Count = 42,
                Name = "Test",
                IsActive = true
            };

            var clone = (SimpleJsonState)original.Clone();

            // Clone should have same values
            Assert.AreEqual(42, clone.Count);
            Assert.AreEqual("Test", clone.Name);
            Assert.AreEqual(true, clone.IsActive);

            // Modifying clone should not affect original
            clone.Count = 100;
            clone.Name = "Modified";

            Assert.AreEqual(42, original.Count);
            Assert.AreEqual("Test", original.Name);
        }

        [Test]
        public void Clone_NestedState_ClonesNestedObjects()
        {
            var original = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState
                {
                    ChildName = "Child",
                    Value = 10
                }
            };

            var clone = (ParentJsonState)original.Clone();

            // Nested object should be cloned
            Assert.AreEqual("Parent", clone.ParentName);
            Assert.AreEqual("Child", clone.Child.ChildName);
            Assert.AreEqual(10, clone.Child.Value);

            // Modifying nested clone should not affect original
            clone.Child.Value = 999;

            Assert.AreEqual(10, original.Child.Value);
        }

        [Test]
        public void Clone_CyclicReference_HandledCorrectly()
        {
            var state1 = new CyclicJsonState { Name = "A" };
            var state2 = new CyclicJsonState { Name = "B" };

            state1.Reference = state2;
            state2.Reference = state1;  // Cycle: A -> B -> A

            var clone1 = (CyclicJsonState)state1.Clone();

            // Clone should preserve structure
            Assert.AreEqual("A", clone1.Name);
            Assert.AreEqual("B", clone1.Reference.Name);

            // Cycle should be preserved in clone
            Assert.AreSame(clone1, clone1.Reference.Reference);
        }

        [Test]
        public void Clone_SelfReference_HandledCorrectly()
        {
            var state = new CyclicJsonState { Name = "Self" };
            state.Reference = state;  // Self-reference

            var clone = (CyclicJsonState)state.Clone();

            Assert.AreEqual("Self", clone.Name);
            Assert.AreSame(clone, clone.Reference);  // Self-reference preserved
        }

        #endregion

        #region CloneWithMap Tests

        [Test]
        public void CloneWithMap_NestedState_MapsAllNestedStates()
        {
            var original = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState
                {
                    ChildName = "Child",
                    Value = 10
                }
            };

            var (clone, map) = original.CloneWithMap();

            // Map should contain both parent and child
            Assert.IsTrue(map.ContainsKey(original));
            Assert.IsTrue(map.ContainsKey(original.Child));

            // Map should point to correct clones
            Assert.AreSame(clone, map[original]);
            Assert.AreSame(((ParentJsonState)clone).Child, map[original.Child]);
        }

        [Test]
        public void CloneWithMap_CyclicReference_MapsAllStates()
        {
            var state1 = new CyclicJsonState { Name = "A" };
            var state2 = new CyclicJsonState { Name = "B" };

            state1.Reference = state2;
            state2.Reference = state1;  // Cycle: A -> B -> A

            var (clone1, map) = state1.CloneWithMap();

            // Map should contain both states
            Assert.IsTrue(map.ContainsKey(state1));
            Assert.IsTrue(map.ContainsKey(state2));

            // Map should point to correct clones
            var clone2 = (CyclicJsonState)map[state2];
            Assert.AreEqual("A", ((CyclicJsonState)map[state1]).Name);
            Assert.AreEqual("B", clone2.Name);

            // Cycle should be preserved in clones
            Assert.AreSame(clone2, ((CyclicJsonState)clone1).Reference);
            Assert.AreSame(clone1, clone2.Reference);
        }

        [Test]
        public void CloneWithMap_CollectionOfStates_MapsAllStates()
        {
            var child1 = new ChildJsonState { ChildName = "Child1", Value = 1 };
            var child2 = new ChildJsonState { ChildName = "Child2", Value = 2 };

            var original = new NestedCollectionJsonState
            {
                Children = new List<ChildJsonState> { child1, child2 },
                ChildrenByName = new Dictionary<string, ChildJsonState>
                {
                    ["one"] = child1,  // Same reference as in list
                    ["two"] = child2
                }
            };

            var (clone, map) = original.CloneWithMap();

            // Map should contain all states
            Assert.IsTrue(map.ContainsKey(original));
            Assert.IsTrue(map.ContainsKey(child1));
            Assert.IsTrue(map.ContainsKey(child2));

            // Cloned children should be in the map
            var clonedChild1 = (ChildJsonState)map[child1];
            var clonedChild2 = (ChildJsonState)map[child2];

            Assert.AreEqual("Child1", clonedChild1.ChildName);
            Assert.AreEqual("Child2", clonedChild2.ChildName);
        }

        [Test]
        public void CloneWithMap_SelfReference_MapsCorrectly()
        {
            var state = new CyclicJsonState { Name = "Self" };
            state.Reference = state;  // Self-reference

            var (clone, map) = state.CloneWithMap();

            Assert.IsTrue(map.ContainsKey(state));
            Assert.AreSame(clone, map[state]);
        }

        [Test]
        public void CloneWithMap_DeeplyNestedState_MapsAllLevels()
        {
            // Create 4 levels deep: level1 -> level2 -> level3 -> level4
            var level4 = new DeeplyNestedState { Name = "Level4", Child = null };
            var level3 = new DeeplyNestedState { Name = "Level3", Child = level4 };
            var level2 = new DeeplyNestedState { Name = "Level2", Child = level3 };
            var level1 = new DeeplyNestedState { Name = "Level1", Child = level2 };

            var (clone, map) = level1.CloneWithMap();

            // Map should contain ALL levels
            Assert.IsTrue(map.ContainsKey(level1), "Level 1 should be in map");
            Assert.IsTrue(map.ContainsKey(level2), "Level 2 should be in map");
            Assert.IsTrue(map.ContainsKey(level3), "Level 3 should be in map");
            Assert.IsTrue(map.ContainsKey(level4), "Level 4 should be in map");

            // Verify the clones are correct
            var clonedLevel1 = (DeeplyNestedState)map[level1];
            var clonedLevel2 = (DeeplyNestedState)map[level2];
            var clonedLevel3 = (DeeplyNestedState)map[level3];
            var clonedLevel4 = (DeeplyNestedState)map[level4];

            Assert.AreEqual("Level1", clonedLevel1.Name);
            Assert.AreEqual("Level2", clonedLevel2.Name);
            Assert.AreEqual("Level3", clonedLevel3.Name);
            Assert.AreEqual("Level4", clonedLevel4.Name);

            // Verify the structure is preserved
            Assert.AreSame(clonedLevel2, clonedLevel1.Child);
            Assert.AreSame(clonedLevel3, clonedLevel2.Child);
            Assert.AreSame(clonedLevel4, clonedLevel3.Child);
            Assert.IsNull(clonedLevel4.Child);
        }

        #endregion

        #region Collection Tests

        [Test]
        public void Clone_ListOfPrimitives_ClonesCorrectly()
        {
            var original = new CollectionJsonState
            {
                Numbers = new List<int> { 1, 2, 3 },
                Names = new List<string> { "a", "b" },
                Scores = new Dictionary<string, int>
                {
                    ["Alice"] = 100,
                    ["Bob"] = 90
                }
            };

            var clone = (CollectionJsonState)original.Clone();

            // Collections should be cloned
            Assert.AreEqual(3, clone.Numbers.Count);
            Assert.AreEqual(2, clone.Names.Count);
            Assert.AreEqual(2, clone.Scores.Count);

            // Modifying clone collections should not affect original
            clone.Numbers.Add(4);
            clone.Scores["Carol"] = 80;

            Assert.AreEqual(3, original.Numbers.Count);
            Assert.AreEqual(2, original.Scores.Count);
        }

        [Test]
        public void Clone_ListOfJsonState_ClonesNestedStates()
        {
            var original = new NestedCollectionJsonState
            {
                Children = new List<ChildJsonState>
                {
                    new ChildJsonState { ChildName = "Child1", Value = 1 },
                    new ChildJsonState { ChildName = "Child2", Value = 2 }
                },
                ChildrenByName = new Dictionary<string, ChildJsonState>
                {
                    ["first"] = new ChildJsonState { ChildName = "First", Value = 10 }
                }
            };

            var clone = (NestedCollectionJsonState)original.Clone();

            // Nested states in collections should be cloned
            Assert.AreEqual("Child1", clone.Children[0].ChildName);
            Assert.AreEqual("First", clone.ChildrenByName["first"].ChildName);

            // Modifying should not affect original
            clone.Children[0].Value = 999;

            Assert.AreEqual(1, original.Children[0].Value);
        }

        #endregion

        #region String Representation / Fingerprint Tests

        [Test]
        public void StringRepresentation_SameState_SameFingerprint()
        {
            var state1 = new SimpleJsonState { Count = 42, Name = "Test", IsActive = true };
            var state2 = new SimpleJsonState { Count = 42, Name = "Test", IsActive = true };

            Assert.AreEqual(state1.ToString(), state2.ToString());
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void StringRepresentation_DifferentState_DifferentFingerprint()
        {
            var state1 = new SimpleJsonState { Count = 42 };
            var state2 = new SimpleJsonState { Count = 43 };

            Assert.AreNotEqual(state1.ToString(), state2.ToString());
            Assert.AreNotEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void StringRepresentation_DictionaryKeyOrder_Deterministic()
        {
            var state1 = new CollectionJsonState
            {
                Scores = new Dictionary<string, int>
                {
                    ["Zebra"] = 1,
                    ["Apple"] = 2,
                    ["Mango"] = 3
                }
            };

            var state2 = new CollectionJsonState
            {
                Scores = new Dictionary<string, int>
                {
                    ["Apple"] = 2,
                    ["Mango"] = 3,
                    ["Zebra"] = 1
                }
            };

            // Despite different insertion order, fingerprints should match
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        #endregion

        #region Lock Tests

        [Test]
        public void Lock_LocksNestedJsonStates()
        {
            var parent = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState { ChildName = "Child", Value = 10 }
            };

            Assert.IsFalse(parent.Locked);
            Assert.IsFalse(parent.Child.Locked);

            parent.Lock();

            Assert.IsTrue(parent.Locked);
            Assert.IsTrue(parent.Child.Locked);
        }

        [Test]
        public void Lock_LocksStatesInCollections()
        {
            var state = new NestedCollectionJsonState
            {
                Children = new List<ChildJsonState>
                {
                    new ChildJsonState { ChildName = "Child1", Value = 1 }
                },
                ChildrenByName = new Dictionary<string, ChildJsonState>
                {
                    ["key"] = new ChildJsonState { ChildName = "Child2", Value = 2 }
                }
            };

            state.Lock();

            Assert.IsTrue(state.Children[0].Locked);
            Assert.IsTrue(state.ChildrenByName["key"].Locked);
        }

        [Test]
        public void Lock_CyclicReference_DoesNotInfiniteLoop()
        {
            var state1 = new CyclicJsonState { Name = "A" };
            var state2 = new CyclicJsonState { Name = "B" };
            state1.Reference = state2;
            state2.Reference = state1;

            // Should not throw or infinite loop
            state1.Lock();

            Assert.IsTrue(state1.Locked);
            Assert.IsTrue(state2.Locked);
        }

        #endregion

        #region Mutation Detection Tests

        [Test]
        public void ValidateNotMutated_UnmodifiedState_NoException()
        {
            var state = new SimpleJsonState { Count = 42 };
            state.Lock();

            // Should not throw
            Assert.DoesNotThrow(() => state.ValidateNotMutated());
        }

        [Test]
        public void ValidateNotMutated_ModifiedState_ThrowsException()
        {
            var state = new SimpleJsonState { Count = 42 };
            state.Lock();

            // Directly modify the locked state (simulating a bug)
            // Note: In practice, public setters don't check Locked for JsonState
            // The framework detects this via ValidateNotMutated
            state.Count = 100;

            Assert.Throws<StateLockedException>(() => state.ValidateNotMutated());
        }

        [Test]
        public void ValidateNotMutated_DisabledDetection_NoException()
        {
            JsonState.EnableMutationDetection = false;

            try
            {
                var state = new SimpleJsonState { Count = 42 };
                state.Lock();
                state.Count = 100;  // Mutate

                // With detection disabled, should not throw
                Assert.DoesNotThrow(() => state.ValidateNotMutated());
            }
            finally
            {
                JsonState.EnableMutationDetection = true;
            }
        }

        [Test]
        public void EnableMutationDetection_DefaultsToTrue()
        {
            // AsyncLocal returns null for unset values, which should default to true
            Assert.IsTrue(JsonState.EnableMutationDetection);
        }

        [Test]
        public void EnableMutationDetection_CanBeToggled()
        {
            var originalValue = JsonState.EnableMutationDetection;

            try
            {
                // Start with default (true)
                Assert.IsTrue(JsonState.EnableMutationDetection);

                // Disable
                JsonState.EnableMutationDetection = false;
                Assert.IsFalse(JsonState.EnableMutationDetection);

                // Re-enable
                JsonState.EnableMutationDetection = true;
                Assert.IsTrue(JsonState.EnableMutationDetection);
            }
            finally
            {
                JsonState.EnableMutationDetection = originalValue;
            }
        }

        [Test]
        public void EnableMutationDetection_ThreadSafe_ParallelExecution()
        {
            // This test verifies that EnableMutationDetection is thread-safe
            // by running multiple tasks that set different values

            var tasks = new System.Threading.Tasks.Task[10];
            var results = new bool[10];

            for (int i = 0; i < tasks.Length; i++)
            {
                int taskIndex = i;
                bool expectedValue = taskIndex % 2 == 0;

                tasks[taskIndex] = System.Threading.Tasks.Task.Run(() =>
                {
                    // Set our own value
                    JsonState.EnableMutationDetection = expectedValue;

                    // Small delay to allow interleaving
                    System.Threading.Thread.Sleep(10);

                    // Read back - should still be our value due to AsyncLocal
                    results[taskIndex] = JsonState.EnableMutationDetection == expectedValue;
                });
            }

            System.Threading.Tasks.Task.WaitAll(tasks);

            // All tasks should have seen their own value
            for (int i = 0; i < results.Length; i++)
            {
                Assert.IsTrue(results[i], $"Task {i} did not see its own EnableMutationDetection value");
            }
        }

        [Test]
        public void ValidateNotMutated_DisabledAtLockTime_NoSnapshotTaken()
        {
            // When mutation detection is disabled at lock time, no snapshot is captured.
            // Even if detection is enabled later, mutations won't be detected because
            // there's no baseline to compare against.
            var originalValue = JsonState.EnableMutationDetection;

            try
            {
                // Start with detection disabled
                JsonState.EnableMutationDetection = false;

                var state = new SimpleJsonState { Count = 42 };
                state.Lock();

                // Enable detection
                JsonState.EnableMutationDetection = true;

                // Mutate
                state.Count = 100;

                // No snapshot was captured at lock time, so validation passes
                // (because there's nothing to compare against)
                Assert.DoesNotThrow(() => state.ValidateNotMutated());
            }
            finally
            {
                JsonState.EnableMutationDetection = originalValue;
            }
        }

        [Test]
        public void ValidateNotMutated_EnabledAtLockTime_DetectsMutation()
        {
            // When mutation detection is enabled at lock time, a snapshot is captured,
            // and mutations are detected even if detection is later disabled
            // (snapshot comparison still happens if snapshot exists)
            var originalValue = JsonState.EnableMutationDetection;

            try
            {
                // Start with detection enabled (default)
                JsonState.EnableMutationDetection = true;

                var state = new SimpleJsonState { Count = 42 };
                state.Lock();

                // Disable detection - but snapshot was already taken
                JsonState.EnableMutationDetection = false;

                // Mutate
                state.Count = 100;

                // Detection is disabled, so no exception
                Assert.DoesNotThrow(() => state.ValidateNotMutated());

                // Re-enable detection
                JsonState.EnableMutationDetection = true;

                // Now mutation should be detected (snapshot was taken at lock time)
                Assert.Throws<StateLockedException>(() => state.ValidateNotMutated());
            }
            finally
            {
                JsonState.EnableMutationDetection = originalValue;
            }
        }

        #endregion

        #region Computed Property Tests

        [Test]
        public void ComputedProperty_Clone_WorksCorrectly()
        {
            var original = new ComputedPropertyJsonState { Value = 5 };

            Assert.AreEqual(10, original.DoubledValue);

            var clone = (ComputedPropertyJsonState)original.Clone();

            Assert.AreEqual(5, clone.Value);
            Assert.AreEqual(10, clone.DoubledValue);
        }

        [Test]
        public void ComputedProperty_Fingerprint_BasedOnSourceProperty()
        {
            var state1 = new ComputedPropertyJsonState { Value = 5 };
            var state2 = new ComputedPropertyJsonState { Value = 5 };

            // Same source value should give same hash
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        #endregion

        #region Edge Cases

        [Test]
        public void Clone_NullProperties_HandledCorrectly()
        {
            var original = new ParentJsonState
            {
                ParentName = null,
                Child = null
            };

            var clone = (ParentJsonState)original.Clone();

            Assert.IsNull(clone.ParentName);
            Assert.IsNull(clone.Child);
        }

        [Test]
        public void Clone_EmptyCollections_HandledCorrectly()
        {
            var original = new CollectionJsonState
            {
                Numbers = new List<int>(),
                Names = new List<string>(),
                Scores = new Dictionary<string, int>()
            };

            var clone = (CollectionJsonState)original.Clone();

            Assert.IsNotNull(clone.Numbers);
            Assert.IsNotNull(clone.Names);
            Assert.IsNotNull(clone.Scores);
            Assert.AreEqual(0, clone.Numbers.Count);
        }

        [Test]
        public void SharedReference_PreservedInList()
        {
            // Create a shared child
            var sharedChild = new ChildJsonState { ChildName = "Shared", Value = 99 };

            var original = new NestedCollectionJsonState
            {
                Children = new List<ChildJsonState> { sharedChild, sharedChild },
                ChildrenByName = null
            };

            var clone = (NestedCollectionJsonState)original.Clone();

            // Reference in List should be preserved (same object twice in list)
            Assert.AreSame(clone.Children[0], clone.Children[1]);
        }

        [Test]
        public void SharedReference_PreservedAcrossCollections()
        {
            // Create a shared child
            var sharedChild = new ChildJsonState { ChildName = "Shared", Value = 99 };

            var original = new NestedCollectionJsonState
            {
                Children = new List<ChildJsonState> { sharedChild, sharedChild },
                ChildrenByName = new Dictionary<string, ChildJsonState>
                {
                    ["shared"] = sharedChild
                }
            };

            var clone = (NestedCollectionJsonState)original.Clone();

            // Reference in List should be preserved
            Assert.AreSame(clone.Children[0], clone.Children[1]);
            
            // Reference across List and Dictionary should also be preserved
            Assert.AreSame(clone.Children[0], clone.ChildrenByName["shared"]);
        }

        #endregion

        #region Additional Robustness Tests

        [Test]
        public void Clone_NumericEdgeCases_HandledCorrectly()
        {
            var original = new NumericEdgeCasesState
            {
                IntValue = int.MaxValue,
                LongValue = long.MinValue,
                DoubleValue = double.Epsilon,
                DecimalValue = 123.456789m
            };

            var clone = (NumericEdgeCasesState)original.Clone();

            Assert.AreEqual(int.MaxValue, clone.IntValue);
            Assert.AreEqual(long.MinValue, clone.LongValue);
            Assert.AreEqual(double.Epsilon, clone.DoubleValue);
            Assert.AreEqual(123.456789m, clone.DecimalValue);
        }

        [Test]
        public void Clone_ThreeWayCycle_HandledCorrectly()
        {
            var a = new ThreeWayCycleState { Name = "A" };
            var b = new ThreeWayCycleState { Name = "B" };
            var c = new ThreeWayCycleState { Name = "C" };

            a.Next = b;
            b.Next = c;
            c.Next = a;  // Cycle: A -> B -> C -> A

            var cloneA = (ThreeWayCycleState)a.Clone();

            Assert.AreEqual("A", cloneA.Name);
            Assert.AreEqual("B", cloneA.Next.Name);
            Assert.AreEqual("C", cloneA.Next.Next.Name);
            Assert.AreSame(cloneA, cloneA.Next.Next.Next);  // Cycle preserved
        }

        [Test]
        public void Clone_DeeplyNested_HandledCorrectly()
        {
            // Create 5 levels of nesting
            var level1 = new DeeplyNestedState { Name = "Level1" };
            var level2 = new DeeplyNestedState { Name = "Level2" };
            var level3 = new DeeplyNestedState { Name = "Level3" };
            var level4 = new DeeplyNestedState { Name = "Level4" };
            var level5 = new DeeplyNestedState { Name = "Level5" };

            level1.Child = level2;
            level2.Child = level3;
            level3.Child = level4;
            level4.Child = level5;

            var clone = (DeeplyNestedState)level1.Clone();

            Assert.AreEqual("Level1", clone.Name);
            Assert.AreEqual("Level2", clone.Child.Name);
            Assert.AreEqual("Level3", clone.Child.Child.Name);
            Assert.AreEqual("Level4", clone.Child.Child.Child.Name);
            Assert.AreEqual("Level5", clone.Child.Child.Child.Child.Name);
            Assert.IsNull(clone.Child.Child.Child.Child.Child);
        }

        [Test]
        public void Clone_IntKeyDictionary_HandledCorrectly()
        {
            var original = new IntKeyDictionaryState
            {
                ItemsById = new Dictionary<int, string>
                {
                    [100] = "Item100",
                    [1] = "Item1",
                    [50] = "Item50"
                }
            };

            var clone = (IntKeyDictionaryState)original.Clone();

            Assert.AreEqual(3, clone.ItemsById.Count);
            Assert.AreEqual("Item100", clone.ItemsById[100]);
            Assert.AreEqual("Item1", clone.ItemsById[1]);
            Assert.AreEqual("Item50", clone.ItemsById[50]);
        }

        [Test]
        public void Clone_NestedCollections_HandledCorrectly()
        {
            var original = new NestedCollectionsState
            {
                Matrix = new List<List<int>>
                {
                    new List<int> { 1, 2, 3 },
                    new List<int> { 4, 5, 6 }
                },
                NestedMaps = new Dictionary<string, Dictionary<string, int>>
                {
                    ["outer1"] = new Dictionary<string, int>
                    {
                        ["inner1"] = 10,
                        ["inner2"] = 20
                    }
                },
                MapOfLists = new Dictionary<string, List<int>>
                {
                    ["list1"] = new List<int> { 1, 2, 3 }
                }
            };

            var clone = (NestedCollectionsState)original.Clone();

            // Verify matrix
            Assert.AreEqual(2, clone.Matrix.Count);
            Assert.AreEqual(3, clone.Matrix[0].Count);
            Assert.AreEqual(1, clone.Matrix[0][0]);
            Assert.AreEqual(6, clone.Matrix[1][2]);

            // Verify nested maps
            Assert.AreEqual(10, clone.NestedMaps["outer1"]["inner1"]);

            // Verify map of lists
            Assert.AreEqual(3, clone.MapOfLists["list1"].Count);

            // Verify independence
            clone.Matrix[0].Add(999);
            Assert.AreEqual(3, original.Matrix[0].Count);
        }

        [Test]
        public void Clone_SpecialTypes_HandledCorrectly()
        {
            var timestamp = new DateTime(2026, 1, 27, 12, 30, 45, DateTimeKind.Utc);
            var guid = Guid.NewGuid();

            var original = new SpecialTypesState
            {
                Timestamp = timestamp,
                Id = guid
            };

            var clone = (SpecialTypesState)original.Clone();

            Assert.AreEqual(timestamp, clone.Timestamp);
            Assert.AreEqual(guid, clone.Id);
        }

        [Test]
        public void StringRepresentation_IntKeyDictionary_Deterministic()
        {
            // Create two states with same data but different insertion order
            var state1 = new IntKeyDictionaryState
            {
                ItemsById = new Dictionary<int, string>
                {
                    [3] = "Three",
                    [1] = "One",
                    [2] = "Two"
                }
            };

            var state2 = new IntKeyDictionaryState
            {
                ItemsById = new Dictionary<int, string>
                {
                    [1] = "One",
                    [2] = "Two",
                    [3] = "Three"
                }
            };

            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void Lock_DeeplyNestedChildren_AllLocked()
        {
            var level1 = new DeeplyNestedState { Name = "L1" };
            var level2 = new DeeplyNestedState { Name = "L2" };
            var level3 = new DeeplyNestedState { Name = "L3" };

            level1.Child = level2;
            level2.Child = level3;

            Assert.IsFalse(level1.Locked);
            Assert.IsFalse(level2.Locked);
            Assert.IsFalse(level3.Locked);

            level1.Lock();

            Assert.IsTrue(level1.Locked);
            Assert.IsTrue(level2.Locked);
            Assert.IsTrue(level3.Locked);
        }

        [Test]
        public void Lock_CyclicReferences_AllLocked()
        {
            var a = new ThreeWayCycleState { Name = "A" };
            var b = new ThreeWayCycleState { Name = "B" };
            var c = new ThreeWayCycleState { Name = "C" };

            a.Next = b;
            b.Next = c;
            c.Next = a;

            a.Lock();

            Assert.IsTrue(a.Locked);
            Assert.IsTrue(b.Locked);
            Assert.IsTrue(c.Locked);
        }

        [Test]
        public void Clone_LockedState_CloneIsUnlocked()
        {
            var original = new SimpleJsonState { Count = 42 };
            original.Lock();

            Assert.IsTrue(original.Locked);

            var clone = (SimpleJsonState)original.Clone();

            Assert.IsFalse(clone.Locked);
        }

        [Test]
        public void ValidateNotMutated_NestedMutation_Detected()
        {
            var parent = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState { ChildName = "Child", Value = 10 }
            };

            parent.Lock();

            // Mutate nested object
            parent.Child.Value = 999;

            Assert.Throws<StateLockedException>(() => parent.ValidateNotMutated());
        }

        [Test]
        public void ValidateNotMutated_CollectionMutation_Detected()
        {
            var state = new CollectionJsonState
            {
                Numbers = new List<int> { 1, 2, 3 },
                Names = null,
                Scores = null
            };

            state.Lock();

            // Mutate collection
            state.Numbers.Add(4);

            Assert.Throws<StateLockedException>(() => state.ValidateNotMutated());
        }

        [Test]
        public void GetStateHash_ConsistentAcrossMultipleCalls()
        {
            var state = new SimpleJsonState { Count = 42, Name = "Test", IsActive = true };

            var hash1 = state.GetStateHash();
            var hash2 = state.GetStateHash();
            var hash3 = state.GetStateHash();

            Assert.AreEqual(hash1, hash2);
            Assert.AreEqual(hash2, hash3);
        }

        [Test]
        public void GetStateHash_DifferentForDifferentNestedValues()
        {
            var state1 = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState { ChildName = "Child", Value = 10 }
            };

            var state2 = new ParentJsonState
            {
                ParentName = "Parent",
                Child = new ChildJsonState { ChildName = "Child", Value = 20 }
            };

            Assert.AreNotEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void Clone_LargeCollection_HandledCorrectly()
        {
            var original = new CollectionJsonState
            {
                Numbers = new List<int>(),
                Names = null,
                Scores = new Dictionary<string, int>()
            };

            // Add 1000 items
            for (int i = 0; i < 1000; i++)
            {
                original.Numbers.Add(i);
                original.Scores[$"key{i}"] = i * 10;
            }

            var clone = (CollectionJsonState)original.Clone();

            Assert.AreEqual(1000, clone.Numbers.Count);
            Assert.AreEqual(1000, clone.Scores.Count);
            Assert.AreEqual(500, clone.Numbers[500]);
            Assert.AreEqual(5000, clone.Scores["key500"]);

            // Verify independence
            clone.Numbers[0] = 9999;
            Assert.AreEqual(0, original.Numbers[0]);
        }

        [Test]
        public void Clone_StateWithAllNullCollections_HandledCorrectly()
        {
            var original = new CollectionJsonState
            {
                Numbers = null,
                Names = null,
                Scores = null
            };

            var clone = (CollectionJsonState)original.Clone();

            Assert.IsNull(clone.Numbers);
            Assert.IsNull(clone.Names);
            Assert.IsNull(clone.Scores);
        }

        [Test]
        public void SharedReference_SameObjectInDictionary_PreservedAcrossKeys()
        {
            var sharedChild = new ChildJsonState { ChildName = "Shared", Value = 42 };

            var original = new NestedCollectionJsonState
            {
                Children = null,
                ChildrenByName = new Dictionary<string, ChildJsonState>
                {
                    ["key1"] = sharedChild,
                    ["key2"] = sharedChild,
                    ["key3"] = sharedChild
                }
            };

            var clone = (NestedCollectionJsonState)original.Clone();

            // All keys should point to the same object
            Assert.AreSame(clone.ChildrenByName["key1"], clone.ChildrenByName["key2"]);
            Assert.AreSame(clone.ChildrenByName["key2"], clone.ChildrenByName["key3"]);
        }

        [Test]
        public void StringRepresentation_EmptyState_Consistent()
        {
            var state1 = new SimpleJsonState();
            var state2 = new SimpleJsonState();

            Assert.AreEqual(state1.ToString(), state2.ToString());
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void Clone_UnsupportedDictionaryKeyType_ThrowsException()
        {
            var state = new UnsupportedKeyState
            {
                Items = new Dictionary<CustomKey, string>
                {
                    [new CustomKey { Id = 1, Name = "A" }] = "Value1"
                }
            };

            var ex = Assert.Throws<UnsupportedDictionaryKeyTypeException>(() => state.Clone());
            Assert.AreEqual(typeof(CustomKey), ex.KeyType);
            Assert.That(ex.Message, Does.Contain("CustomKey"));
            Assert.That(ex.Message, Does.Contain("string, int, long, Guid"));
        }

        #endregion

        #region JsonAtomic Tests

        [Test]
        public void JsonAtomic_Clone_PreservesAtomicReference()
        {
            var content = new byte[] { 1, 2, 3, 4, 5 };
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            var clone = (ImageState)original.Clone();

            // Content should be the same reference (shallow copy)
            Assert.AreSame(original.Content, clone.Content);
            Assert.AreEqual("test.jpg", clone.Name);
        }

        [Test]
        public void JsonAtomic_Clone_OtherPropertiesAreIndependent()
        {
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 1, 2, 3 }
            };

            var clone = (ImageState)original.Clone();

            // Modify non-atomic property on clone
            clone.Name = "modified.jpg";

            // Original should be unchanged
            Assert.AreEqual("test.jpg", original.Name);
        }

        [Test]
        public void JsonAtomic_Fingerprint_UsesFingerprintProperty()
        {
            var state1 = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 1, 2, 3, 4, 5 }
            };

            var state2 = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 1, 2, 3, 4, 5 }  // Same content, different array instance
            };

            // Same fingerprint because ContentFingerprint computes same hash
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void JsonAtomic_Fingerprint_DifferentContentDifferentHash()
        {
            var state1 = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 1, 2, 3 }
            };

            var state2 = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 4, 5, 6 }  // Different content
            };

            // Different fingerprints
            Assert.AreNotEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void JsonAtomic_Fingerprint_NullContentHandled()
        {
            var state1 = new ImageState
            {
                Name = "test.jpg",
                Content = null
            };

            var state2 = new ImageState
            {
                Name = "test.jpg",
                Content = null
            };

            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void JsonAtomic_NestedState_AtomicCopiedRecursively()
        {
            var image1Content = new byte[] { 1, 2, 3 };
            var image2Content = new byte[] { 4, 5, 6 };

            var original = new AlbumState
            {
                AlbumName = "Vacation",
                Images = new List<ImageState>
                {
                    new ImageState { Name = "img1.jpg", Content = image1Content },
                    new ImageState { Name = "img2.jpg", Content = image2Content }
                }
            };

            var clone = (AlbumState)original.Clone();

            // Atomic properties in nested objects should be same reference
            Assert.AreSame(image1Content, clone.Images[0].Content);
            Assert.AreSame(image2Content, clone.Images[1].Content);

            // But the ImageState objects themselves are different
            Assert.AreNotSame(original.Images[0], clone.Images[0]);
        }

        [Test]
        public void JsonAtomic_StringContent_Works()
        {
            var original = new DocumentState
            {
                Title = "My Doc",
                Body = "This is a very long document body..."
            };

            var clone = (DocumentState)original.Clone();

            // Atomic string property should be same reference
            Assert.AreSame(original.Body, clone.Body);
            Assert.AreEqual("My Doc", clone.Title);
        }

        [Test]
        public void JsonAtomic_Fingerprint_UsesCustomFingerprint()
        {
            var state = new DocumentState
            {
                Title = "Doc",
                Body = "Hello World"
            };

            var fingerprint = state.ToString();

            // Fingerprint should contain "len:11" (from BodyFingerprint)
            Assert.IsTrue(fingerprint.Contains("len:11"), 
                $"Expected fingerprint to contain 'len:11', but was: {fingerprint}");
            
            // Fingerprint should NOT contain the actual body text
            Assert.IsFalse(fingerprint.Contains("Hello World"),
                "Fingerprint should not contain actual body content");
        }

        [Test]
        public void JsonAtomic_Lock_DoesNotAffectAtomicProperties()
        {
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 1, 2, 3 }
            };

            original.Lock();

            // We can still access atomic property after lock
            Assert.IsNotNull(original.Content);
            Assert.AreEqual(3, original.Content.Length);
        }

        [Test]
        public void JsonAtomic_MutationDetection_DetectsViaFingerprintProperty()
        {
            // Note: Even though atomic properties themselves are excluded from serialization,
            // mutation IS detected via the fingerprint property (which computes from the atomic).
            // This is actually desirable - if user mutates atomic content, fingerprint changes.
            var content = new byte[] { 1, 2, 3 };
            var state = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            state.Lock();

            // Mutating atomic content changes the fingerprint
            content[0] = 99;

            // Mutation is detected because ContentFingerprint changed
            Assert.Throws<StateLockedException>(() => state.ValidateNotMutated());
        }

        [Test]
        public void JsonAtomic_InDictionaryValues_AtomicCopiedRecursively()
        {
            var image1Content = new byte[] { 1, 2, 3 };
            var image2Content = new byte[] { 4, 5, 6 };

            var original = new GalleryState
            {
                GalleryName = "My Gallery",
                ImagesByName = new Dictionary<string, ImageState>
                {
                    ["photo1"] = new ImageState { Name = "photo1.jpg", Content = image1Content },
                    ["photo2"] = new ImageState { Name = "photo2.jpg", Content = image2Content }
                }
            };

            var clone = (GalleryState)original.Clone();

            // Atomic properties in dictionary values should be same reference
            Assert.AreSame(image1Content, clone.ImagesByName["photo1"].Content);
            Assert.AreSame(image2Content, clone.ImagesByName["photo2"].Content);

            // But the ImageState objects themselves are different
            Assert.AreNotSame(original.ImagesByName["photo1"], clone.ImagesByName["photo1"]);
        }

        [Test]
        public void JsonAtomic_MultipleAtomicProperties_AllCopiedByReference()
        {
            var content = new byte[] { 1, 2, 3 };
            var metadata = new byte[] { 10, 20, 30 };
            var thumbnail = new byte[] { 100, 200 };

            var original = new MultiAtomicState
            {
                Name = "multi.dat",
                Content = content,
                Metadata = metadata,
                Thumbnail = thumbnail
            };

            var clone = (MultiAtomicState)original.Clone();

            // All atomic properties should be same reference
            Assert.AreSame(content, clone.Content);
            Assert.AreSame(metadata, clone.Metadata);
            Assert.AreSame(thumbnail, clone.Thumbnail);

            // Non-atomic property should be independent
            clone.Name = "modified.dat";
            Assert.AreEqual("multi.dat", original.Name);
        }

        [Test]
        public void JsonAtomic_MultipleAtomicProperties_FingerprintIncludesAll()
        {
            var state1 = new MultiAtomicState
            {
                Name = "test",
                Content = new byte[] { 1, 2, 3 },
                Metadata = new byte[] { 4, 5, 6 },
                Thumbnail = new byte[] { 7, 8, 9 }
            };

            var state2 = new MultiAtomicState
            {
                Name = "test",
                Content = new byte[] { 1, 2, 3 },
                Metadata = new byte[] { 4, 5, 6 },
                Thumbnail = new byte[] { 7, 8, 9 }  // Same content
            };

            var state3 = new MultiAtomicState
            {
                Name = "test",
                Content = new byte[] { 1, 2, 3 },
                Metadata = new byte[] { 4, 5, 6 },
                Thumbnail = new byte[] { 99, 99, 99 }  // Different thumbnail
            };

            // Same content should have same hash
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());

            // Different thumbnail should have different hash
            Assert.AreNotEqual(state1.GetStateHash(), state3.GetStateHash());
        }

        [Test]
        public void JsonAtomic_SharedReferenceAcrossObjects_BothPreserved()
        {
            var sharedContent = new byte[] { 1, 2, 3, 4, 5 };

            // Two ImageState objects share the same byte[] reference
            var image1 = new ImageState { Name = "img1.jpg", Content = sharedContent };
            var image2 = new ImageState { Name = "img2.jpg", Content = sharedContent };

            var original = new AlbumState
            {
                AlbumName = "Shared Content Album",
                Images = new List<ImageState> { image1, image2 }
            };

            var clone = (AlbumState)original.Clone();

            // Both cloned images should still reference the same byte[]
            Assert.AreSame(sharedContent, clone.Images[0].Content);
            Assert.AreSame(sharedContent, clone.Images[1].Content);
            Assert.AreSame(clone.Images[0].Content, clone.Images[1].Content);
        }

        [Test]
        public void JsonAtomic_InvalidFingerprintProperty_DoesNotThrowOnClone()
        {
            // When fingerprint property doesn't exist, clone should still work
            // (atomic property just won't have a fingerprint in serialization)
            var original = new InvalidFingerprintRefState
            {
                Name = "test",
                Data = new byte[] { 1, 2, 3 }
            };

            // Clone should succeed - atomic property copied by reference
            var clone = (InvalidFingerprintRefState)original.Clone();

            Assert.AreSame(original.Data, clone.Data);
            Assert.AreEqual("test", clone.Name);
        }

        [Test]
        public void JsonAtomic_InvalidFingerprintProperty_FingerprintExcludesAtomicData()
        {
            var state1 = new InvalidFingerprintRefState
            {
                Name = "test",
                Data = new byte[] { 1, 2, 3 }
            };

            var state2 = new InvalidFingerprintRefState
            {
                Name = "test",
                Data = new byte[] { 99, 99, 99 }  // Different data
            };

            // Since fingerprint property doesn't exist, atomic data is excluded
            // and not replaced by anything - states should have same hash
            Assert.AreEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void JsonAtomic_ThrowingFingerprintProperty_CloneThrowsWithClearMessage()
        {
            // Fingerprint properties are expected not to throw.
            // If they do, we wrap the exception with a clear message.
            var original = new ThrowingFingerprintState
            {
                Name = "test",
                Content = new byte[] { 1, 2, 3 }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => original.Clone());
            Assert.IsTrue(ex.Message.Contains("ContentFingerprint"), 
                $"Exception should mention the property name. Was: {ex.Message}");
            Assert.IsTrue(ex.Message.Contains("should not throw"), 
                $"Exception should explain the contract. Was: {ex.Message}");
        }

        [Test]
        public void JsonAtomic_ThrowingFingerprintProperty_FingerprintThrowsWithClearMessage()
        {
            var state = new ThrowingFingerprintState
            {
                Name = "test",
                Content = new byte[] { 1, 2, 3 }
            };

            // GetStateHash/ToString wraps the exception with a clear message
            var ex = Assert.Throws<InvalidOperationException>(() => state.GetStateHash());
            Assert.IsTrue(ex.Message.Contains("ContentFingerprint"),
                $"Exception should mention the property name. Was: {ex.Message}");
            Assert.IsTrue(ex.Message.Contains("should not throw"),
                $"Exception should explain the contract. Was: {ex.Message}");
        }

        [Test]
        public void JsonAtomic_NullVsEmptyFingerprint_TreatedDifferently()
        {
            var state1 = new NullableFingerprint​State
            {
                Name = "test",
                Data = null,
                ReturnEmptyInsteadOfNull = false  // Returns null
            };

            var state2 = new NullableFingerprint​State
            {
                Name = "test",
                Data = null,
                ReturnEmptyInsteadOfNull = true  // Returns ""
            };

            // Null and empty string should produce different fingerprints
            Assert.AreNotEqual(state1.GetStateHash(), state2.GetStateHash());
        }

        [Test]
        public void JsonAtomic_WithCyclicReference_HandledCorrectly()
        {
            var content1 = new byte[] { 1, 2, 3 };
            var content2 = new byte[] { 4, 5, 6 };

            var state1 = new CyclicAtomicState { Name = "A", Content = content1 };
            var state2 = new CyclicAtomicState { Name = "B", Content = content2 };

            state1.Reference = state2;
            state2.Reference = state1;  // Cycle: A -> B -> A

            var clone1 = (CyclicAtomicState)state1.Clone();

            // Verify structure preserved
            Assert.AreEqual("A", clone1.Name);
            Assert.AreEqual("B", clone1.Reference.Name);
            Assert.AreSame(clone1, clone1.Reference.Reference);  // Cycle preserved

            // Verify atomic properties copied by reference
            Assert.AreSame(content1, clone1.Content);
            Assert.AreSame(content2, clone1.Reference.Content);
        }

        [Test]
        public void JsonAtomic_WithCyclicReference_FingerprintWorks()
        {
            var state1 = new CyclicAtomicState
            {
                Name = "A",
                Content = new byte[] { 1, 2, 3 }
            };
            state1.Reference = state1;  // Self-reference

            // Should not throw or infinite loop
            var hash = state1.GetStateHash();
            Assert.IsNotNull(hash);
        }

        [Test]
        public void JsonAtomic_ExplicitVerifyAtomicExcludedFromFingerprint()
        {
            // Create state with known atomic content
            var state = new ImageState
            {
                Name = "test.jpg",
                Content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
            };

            var fingerprint = state.ToString();

            // The raw bytes should NOT appear in the fingerprint
            Assert.IsFalse(fingerprint.Contains("DEADBEEF", StringComparison.OrdinalIgnoreCase),
                "Atomic content should not appear directly in fingerprint");

            // But the hash of the content (via ContentFingerprint) should be there
            var expectedHash = state.ContentFingerprint;
            Assert.IsTrue(fingerprint.Contains(expectedHash),
                $"Expected fingerprint to contain hash '{expectedHash}'");
        }

        [Test]
        public void JsonAtomic_CloneThenModifyOriginal_CloneSharesReference()
        {
            var content = new byte[] { 1, 2, 3 };
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            var clone = (ImageState)original.Clone();

            // Verify they share the same reference
            Assert.AreSame(original.Content, clone.Content);

            // Modify the content through original
            original.Content[0] = 99;

            // Clone sees the change because they share the reference
            // This is EXPECTED behavior for atomic properties (shallow copy)
            Assert.AreEqual(99, clone.Content[0]);
        }

        [Test]
        public void JsonAtomic_CloneThenReplaceOriginalReference_CloneUnaffected()
        {
            var content = new byte[] { 1, 2, 3 };
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            var clone = (ImageState)original.Clone();

            // Replace the reference on original (not mutate)
            original.Content = new byte[] { 99, 99, 99 };

            // Clone should still have the old reference
            Assert.AreEqual(1, clone.Content[0]);
            Assert.AreNotSame(original.Content, clone.Content);
        }

        [Test]
        public void JsonAtomic_DeeplyNested_AtomicCopiedAtAllLevels()
        {
            // Create 3 levels: Library > Album > Image
            var imageContent1 = new byte[] { 1, 2, 3 };
            var imageContent2 = new byte[] { 4, 5, 6 };
            var imageContent3 = new byte[] { 7, 8, 9 };

            var original = new LibraryState
            {
                LibraryName = "My Library",
                Albums = new List<AlbumState>
                {
                    new AlbumState
                    {
                        AlbumName = "Album1",
                        Images = new List<ImageState>
                        {
                            new ImageState { Name = "img1.jpg", Content = imageContent1 },
                            new ImageState { Name = "img2.jpg", Content = imageContent2 }
                        }
                    },
                    new AlbumState
                    {
                        AlbumName = "Album2",
                        Images = new List<ImageState>
                        {
                            new ImageState { Name = "img3.jpg", Content = imageContent3 }
                        }
                    }
                }
            };

            var clone = (LibraryState)original.Clone();

            // Verify structure
            Assert.AreEqual("My Library", clone.LibraryName);
            Assert.AreEqual(2, clone.Albums.Count);
            Assert.AreEqual("Album1", clone.Albums[0].AlbumName);
            Assert.AreEqual(2, clone.Albums[0].Images.Count);

            // Verify atomic properties at deepest level are same reference
            Assert.AreSame(imageContent1, clone.Albums[0].Images[0].Content);
            Assert.AreSame(imageContent2, clone.Albums[0].Images[1].Content);
            Assert.AreSame(imageContent3, clone.Albums[1].Images[0].Content);

            // Verify non-atomic properties are independent
            clone.Albums[0].Images[0].Name = "modified.jpg";
            Assert.AreEqual("img1.jpg", original.Albums[0].Images[0].Name);
        }

        [Test]
        public void JsonAtomic_DeeplyNested_LockLocksAllLevels()
        {
            var original = new LibraryState
            {
                LibraryName = "My Library",
                Albums = new List<AlbumState>
                {
                    new AlbumState
                    {
                        AlbumName = "Album1",
                        Images = new List<ImageState>
                        {
                            new ImageState { Name = "img1.jpg", Content = new byte[] { 1 } }
                        }
                    }
                }
            };

            original.Lock();

            Assert.IsTrue(original.Locked);
            Assert.IsTrue(original.Albums[0].Locked);
            Assert.IsTrue(original.Albums[0].Images[0].Locked);
        }

        [Test]
        public void JsonAtomic_DefaultJsonSerializerIncludesAtomicProperties()
        {
            // Default JsonSerializer.Serialize() should INCLUDE atomic properties
            // This is important for users who want to persist full state to disk
            var content = new byte[] { 1, 2, 3, 4, 5 };
            var state = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            // Use default JsonSerializer - atomic properties should be included
            var json = System.Text.Json.JsonSerializer.Serialize(state);

            // The JSON should contain the Content property with base64-encoded bytes
            Assert.IsTrue(json.Contains("\"Content\""), 
                "Default serialization should include atomic Content property");
            Assert.IsTrue(json.Contains("AQIDBAU="),  // base64 of [1,2,3,4,5]
                "Default serialization should include the actual byte content as base64");
            Assert.IsTrue(json.Contains("\"Name\""),
                "Default serialization should include non-atomic properties");
        }

        [Test]
        public void JsonAtomic_DefaultJsonSerializerRoundTrip_CreatesNewInstances()
        {
            // When using default serialization for persistence, deserialize creates new objects
            var content = new byte[] { 1, 2, 3, 4, 5 };
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            // Round-trip through default JsonSerializer
            var json = System.Text.Json.JsonSerializer.Serialize(original);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ImageState>(json);

            // Content should be equal but NOT the same reference (different instance)
            Assert.AreEqual(original.Content.Length, deserialized.Content.Length);
            Assert.AreEqual(original.Content[0], deserialized.Content[0]);
            Assert.AreNotSame(original.Content, deserialized.Content,
                "Deserialized content should be a new instance, not same reference");
            Assert.AreEqual(original.Name, deserialized.Name);
        }

        [Test]
        public void JsonAtomic_CloneVsDefaultSerializer_DifferentBehavior()
        {
            // This test explicitly shows the difference between Clone() and default serialization
            var content = new byte[] { 1, 2, 3, 4, 5 };
            var original = new ImageState
            {
                Name = "test.jpg",
                Content = content
            };

            // Clone() - copies reference (same instance)
            var clone = (ImageState)original.Clone();
            Assert.AreSame(original.Content, clone.Content,
                "Clone() should copy reference for atomic properties");

            // Default JsonSerializer - creates new instance
            var json = System.Text.Json.JsonSerializer.Serialize(original);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ImageState>(json);
            Assert.AreNotSame(original.Content, deserialized.Content,
                "Default serialization should create new instance");

            // Both have the same values
            CollectionAssert.AreEqual(original.Content, clone.Content);
            CollectionAssert.AreEqual(original.Content, deserialized.Content);
        }

        #endregion
    }
}

