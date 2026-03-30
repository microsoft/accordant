// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture]
    public class DescriptorTests
    {
        [Test]
        public void DictionaryAtomicStateTests()
        {
            var map = new DictionaryAtomicState<string>();

            map["hello"] = "world";

            Assert.IsTrue(map.ContainsKey("hello"));
            Assert.IsTrue(map["hello"] == "world");
            Assert.IsTrue(map.Count == 1);

            map.Remove("hello");
            Assert.IsTrue(!map.ContainsKey("hello"));

            map.Lock();

            Assert.Throws<StateLockedException>(() => map["a"] = "b");
        }

        [Test]
        public void ListAtomicStateTests()
        {
            var list = new ListAtomicState<string>()
            {
                "hello"
            };

            Assert.IsTrue(list.Contains("hello"));
            Assert.IsTrue(list.Count == 1);
            Assert.IsTrue(list[0] == "hello");

            list.RemoveAt(0);

            Assert.IsTrue(!list.Contains("hello"));
            Assert.IsTrue(list.Count == 0);

            list.Lock();

            Assert.Throws<StateLockedException>(() => list.Add("a"));
        }

        [Test]
        public void DictionaryStateCloneTests()
        {
            {
                var dictState = new DictionaryState<AtomicState<string>>();
                dictState["k"] = new AtomicState<string>("v");

                var clonedDictState = (DictionaryState<AtomicState<string>>)dictState.Clone();
                Assert.IsTrue(dictState.GetStateHash() == clonedDictState.GetStateHash());

                clonedDictState["k2"] = new AtomicState<string>("v2");

                Assert.IsTrue(dictState.GetStateHash() != clonedDictState.GetStateHash());
                Assert.IsTrue(dictState.Count == 1);
                Assert.IsTrue(clonedDictState.Count == 2);
            }

            {
                var atomicDictState = new DictionaryAtomicState<string>();
                atomicDictState["k"] = "v";

                var clonedAtomicDictState = (DictionaryAtomicState<string>)atomicDictState.Clone();
                Assert.IsTrue(atomicDictState.GetStateHash() == clonedAtomicDictState.GetStateHash());

                clonedAtomicDictState["k2"] = "v2";

                Assert.IsTrue(atomicDictState.GetStateHash() != clonedAtomicDictState.GetStateHash());
                Assert.IsTrue(atomicDictState.Count == 1);
                Assert.IsTrue(clonedAtomicDictState.Count == 2);
            }
        }

        [Test]
        public void ListStateCloneTests()
        {
            {
                var listState = new ListState<AtomicState<string>>();
                listState.Add(new AtomicState<string>("v"));

                var clonedListState = (ListState<AtomicState<string>>)listState.Clone();
                Assert.IsTrue(listState.GetStateHash() == clonedListState.GetStateHash());

                clonedListState.Add(new AtomicState<string>("v2"));

                Assert.IsTrue(listState.GetStateHash() != clonedListState.GetStateHash());
                Assert.IsTrue(listState.Count == 1);
                Assert.IsTrue(clonedListState.Count == 2);
            }

            {
                var atomicListState = new ListAtomicState<string>();
                atomicListState.Add("v");

                var clonedAtomicListState = (ListAtomicState<string>)atomicListState.Clone();
                Assert.IsTrue(atomicListState.GetStateHash() == clonedAtomicListState.GetStateHash());

                clonedAtomicListState.Add("v2");

                Assert.IsTrue(atomicListState.GetStateHash() != clonedAtomicListState.GetStateHash());
                Assert.IsTrue(atomicListState.Count == 1);
                Assert.IsTrue(clonedAtomicListState.Count == 2);
            }
        }

        [Test]
        public void ListStateIListTests()
        {
            // Add
            var listState = new ListState<AtomicState<string>>();
            var element1 = new AtomicState<string>("Test1");
            listState.Add(element1);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element1, listState[0]);

            // Remove
            listState = new ListState<AtomicState<string>>();
            var element2 = new AtomicState<string>("Test2");
            listState.Add(element1);
            listState.Add(element2);
            listState.Remove(element1);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element2, listState[0]);

            // Indexer
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            listState[0] = element2;
            Assert.AreEqual(element2, listState[0]);

            // Contains
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            Assert.IsTrue(listState.Contains(element1));
            Assert.IsFalse(listState.Contains(element2));

            // IndexOf
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            listState.Add(element2);
            Assert.AreEqual(1, listState.IndexOf(element2));

            // Insert
            listState = new ListState<AtomicState<string>>();
            listState.Add(element2);
            listState.Insert(0, element1);
            Assert.AreEqual(0, listState.IndexOf(element1));
            Assert.AreEqual(1, listState.IndexOf(element2));

            // RemoveAt
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            listState.Add(element2);
            listState.RemoveAt(0);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element2, listState[0]);

            // Clear
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            listState.Add(element2);
            listState.Clear();
            Assert.AreEqual(0, listState.Count);

            // CopyTo
            listState = new ListState<AtomicState<string>>();
            listState.Add(element1);
            listState.Add(element2);
            var array = new AtomicState<string>[2];
            listState.CopyTo(array, 0);
            Assert.AreEqual(element1, array[0]);
            Assert.AreEqual(element2, array[1]);

            // IsReadOnly
            listState = new ListState<AtomicState<string>>();
            Assert.IsFalse(listState.IsReadOnly);
        }

        [Test]
        public void ListAtomicStateIListTests()
        {
            // Add
            var listState = new ListAtomicState<string>();
            var element1 = "Test1";
            listState.Add(element1);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element1, listState[0]);

            // Remove
            listState = new ListAtomicState<string>();
            var element2 = "Test2";
            listState.Add(element1);
            listState.Add(element2);
            listState.Remove(element1);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element2, listState[0]);

            // Indexer
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            listState[0] = element2;
            Assert.AreEqual(element2, listState[0]);

            // Contains
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            Assert.IsTrue(listState.Contains(element1));
            Assert.IsFalse(listState.Contains(element2));

            // IndexOf
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            listState.Add(element2);
            Assert.AreEqual(1, listState.IndexOf(element2));

            // Insert
            listState = new ListAtomicState<string>();
            listState.Add(element2);
            listState.Insert(0, element1);
            Assert.AreEqual(0, listState.IndexOf(element1));
            Assert.AreEqual(1, listState.IndexOf(element2));

            // RemoveAt
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            listState.Add(element2);
            listState.RemoveAt(0);
            Assert.AreEqual(1, listState.Count);
            Assert.AreEqual(element2, listState[0]);

            // Clear
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            listState.Add(element2);
            listState.Clear();
            Assert.AreEqual(0, listState.Count);

            // CopyTo
            listState = new ListAtomicState<string>();
            listState.Add(element1);
            listState.Add(element2);
            var array = new string[2];
            listState.CopyTo(array, 0);
            Assert.AreEqual(element1, array[0]);
            Assert.AreEqual(element2, array[1]);

            // IsReadOnly
            listState = new ListAtomicState<string>();
            Assert.IsFalse(listState.IsReadOnly);
        }

        [Test]
        public void DictionaryStateIDictonaryTests()
        {
            // Add and Indexer
            var dictionaryState = new DictionaryState<AtomicState<string>>();
            var key1 = "Key1";
            var value1 = new AtomicState<string>("Value1");
            dictionaryState.Add(key1, value1);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value1, dictionaryState[key1]);

            // Remove
            var key2 = "Key2";
            var value2 = new AtomicState<string>("Value2");
            dictionaryState.Add(key2, value2);
            dictionaryState.Remove(key1);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value2, dictionaryState[key2]);

            // ContainsKey
            dictionaryState = new DictionaryState<AtomicState<string>>();
            dictionaryState.Add(key1, value1);
            Assert.IsTrue(dictionaryState.ContainsKey(key1));
            Assert.IsFalse(dictionaryState.ContainsKey(key2));

            // TryGetValue
            dictionaryState = new DictionaryState<AtomicState<string>>();
            dictionaryState.Add(key1, value1);
            Assert.IsTrue(dictionaryState.TryGetValue(key1, out var retrievedValue));
            Assert.AreEqual(value1, retrievedValue);
            Assert.IsFalse(dictionaryState.TryGetValue(key2, out _));

            // Keys and Values
            dictionaryState = new DictionaryState<AtomicState<string>>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            var keys = dictionaryState.Keys;
            var values = dictionaryState.Values;
            CollectionAssert.AreEquivalent(new[] { key1, key2 }, keys);
            CollectionAssert.AreEquivalent(new[] { value1, value2 }, values);

            // Add KeyValuePair
            dictionaryState = new DictionaryState<AtomicState<string>>();
            var kvp = new KeyValuePair<string, AtomicState<string>>(key1, value1);
            dictionaryState.Add(kvp);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value1, dictionaryState[key1]);

            // Contains KeyValuePair
            Assert.IsTrue(dictionaryState.Contains(kvp));
            var kvp2 = new KeyValuePair<string, AtomicState<string>>(key2, value2);
            Assert.IsFalse(dictionaryState.Contains(kvp2));

            // Remove KeyValuePair
            dictionaryState.Remove(kvp);
            Assert.AreEqual(0, dictionaryState.Count);

            // Clear
            dictionaryState = new DictionaryState<AtomicState<string>>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            dictionaryState.Clear();
            Assert.AreEqual(0, dictionaryState.Count);

            // CopyTo
            dictionaryState = new DictionaryState<AtomicState<string>>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            var array = new KeyValuePair<string, AtomicState<string>>[2];
            dictionaryState.CopyTo(array, 0);
            CollectionAssert.Contains(array, new KeyValuePair<string, AtomicState<string>>(key1, value1));
            CollectionAssert.Contains(array, new KeyValuePair<string, AtomicState<string>>(key2, value2));

            // IsReadOnly
            dictionaryState = new DictionaryState<AtomicState<string>>();
            Assert.IsFalse(dictionaryState.IsReadOnly);
        }

        [Test]
        public void DictionaryAtomicStateIDictionaryTests()
        {
            // Add and Indexer
            var dictionaryState = new DictionaryAtomicState<string>();
            var key1 = "Key1";
            var value1 = "Value1";
            dictionaryState.Add(key1, value1);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value1, dictionaryState[key1]);

            // Remove
            var key2 = "Key2";
            var value2 = "Value2";
            dictionaryState.Add(key2, value2);
            dictionaryState.Remove(key1);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value2, dictionaryState[key2]);

            // ContainsKey
            dictionaryState = new DictionaryAtomicState<string>();
            dictionaryState.Add(key1, value1);
            Assert.IsTrue(dictionaryState.ContainsKey(key1));
            Assert.IsFalse(dictionaryState.ContainsKey(key2));

            // TryGetValue
            dictionaryState = new DictionaryAtomicState<string>();
            dictionaryState.Add(key1, value1);
            Assert.IsTrue(dictionaryState.TryGetValue(key1, out var retrievedValue));
            Assert.AreEqual(value1, retrievedValue);
            Assert.IsFalse(dictionaryState.TryGetValue(key2, out _));

            // Keys and Values
            dictionaryState = new DictionaryAtomicState<string>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            var keys = dictionaryState.Keys;
            var values = dictionaryState.Values;
            CollectionAssert.AreEquivalent(new[] { key1, key2 }, keys);
            CollectionAssert.AreEquivalent(new[] { value1, value2 }, values);

            // Add KeyValuePair
            dictionaryState = new DictionaryAtomicState<string>();
            var kvp = new KeyValuePair<string, string>(key1, value1);
            dictionaryState.Add(kvp);
            Assert.AreEqual(1, dictionaryState.Count);
            Assert.AreEqual(value1, dictionaryState[key1]);

            // Contains KeyValuePair
            Assert.IsTrue(dictionaryState.Contains(kvp));
            var kvp2 = new KeyValuePair<string, string>(key2, value2);
            Assert.IsFalse(dictionaryState.Contains(kvp2));

            // Remove KeyValuePair
            dictionaryState.Remove(kvp);
            Assert.AreEqual(0, dictionaryState.Count);

            // Clear
            dictionaryState = new DictionaryAtomicState<string>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            dictionaryState.Clear();
            Assert.AreEqual(0, dictionaryState.Count);

            // CopyTo
            dictionaryState = new DictionaryAtomicState<string>();
            dictionaryState.Add(key1, value1);
            dictionaryState.Add(key2, value2);
            var array = new KeyValuePair<string, string>[2];
            dictionaryState.CopyTo(array, 0);
            CollectionAssert.Contains(array, new KeyValuePair<string, string>(key1, value1));
            CollectionAssert.Contains(array, new KeyValuePair<string, string>(key2, value2));

            // IsReadOnly
            dictionaryState = new DictionaryAtomicState<string>();
            Assert.IsFalse(dictionaryState.IsReadOnly);
        }
    }
}
