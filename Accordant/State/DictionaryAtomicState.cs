// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// DictionaryAtomicState is an object mapping strings to values that are internally wrapped
    /// in <see cref="AtomicState{TValue}"/> objects.
    /// </summary>
    public class DictionaryAtomicState<TValue> : State, IDictionary<string, TValue>
    {
        private DictionaryState<AtomicState<TValue>> dictionaryState =
            new DictionaryState<AtomicState<TValue>>();

        /// <summary>
        /// Consturcts an instance of this class.
        /// </summary>
        public DictionaryAtomicState() : this(null)
        {
        }

        /// <summary>
        /// Constructs an instance of DictionaryAtomicState given an optional dictionary
        /// mapping strings to objects (wraps each object inside AtomicState to make the value atomic).
        /// </summary>
        /// <param name="dict"></param>
        public DictionaryAtomicState(Dictionary<string, TValue> dict = null)
        {
            dictionaryState = new DictionaryState<AtomicState<TValue>>();

            if (dict != null)
            {
                foreach (KeyValuePair<string, TValue> kvp in dict)
                {
                    dictionaryState[kvp.Key] = new AtomicState<TValue>(kvp.Value);
                }
            }
        }

        /// <summary>
        /// Gets the value given key. Sets (create or update) the value object
        /// with the given key if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public TValue this[string key]
        {
            get => dictionaryState[key].Value;
            set => dictionaryState[key] = new AtomicState<TValue>(value);
        }

        ///// <summary>
        ///// Returns the string keys of the dictionary.
        ///// </summary>
        //public IEnumerable<string> Keys => dictionaryState.Keys;

        ///// <summary>
        ///// Returns the values of the dictionary.
        ///// </summary>
        //public IEnumerable<TValue> Values => dictionaryState.Values.Select(v => ((AtomicState<TValue>)v).Value);

        /// <summary>
        /// Returns the number of key/value pairs in the dictionary.
        /// </summary>
        public int Count => dictionaryState.Count;

        /// <inheritdoc/>
        public ICollection<string> Keys => dictionaryState.Keys;

        /// <inheritdoc/>
        public ICollection<TValue> Values => dictionaryState.Values.Select(v => v.Value).ToList();

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <summary>
        /// Indicates whether the dictionary contains the given key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(string key)
        {
            return dictionaryState.ContainsKey(key);
        }

        /// <summary>
        /// Attempts to fetch the value if the given key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public bool TryGetValue(string key, out TValue value)
        {
            value = default;

            if (!dictionaryState.TryGetValue(key, out AtomicState<TValue> atomicStateValue))
            {
                return false;
            }

            value = atomicStateValue.Value;
            return true;
        }

        /// <summary>
        /// Returns an enumerator for the key/value pairs in this object.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            return dictionaryState
                .Select(kv => new KeyValuePair<string, TValue>(
                    kv.Key,
                    kv.Value.Value))
                .GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator for the key/value pairs in this object.
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return dictionaryState
                .Select(kv => new KeyValuePair<string, TValue>(
                    kv.Key,
                    kv.Value.Value))
                .GetEnumerator();
        }

        /// <summary>
        /// Removes the given key from the dictionary if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public bool Remove(string key)
        {
            return dictionaryState.Remove(key);
        }

        /// <summary>
        /// Deep clones dictionary by cloning the underlying DictionaryState<typeparamref name="TValue"/> object.
        /// </summary>
        /// <returns></returns>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clonedDictAtomicState = new DictionaryAtomicState<TValue>();
            clonedMap[this] = clonedDictAtomicState;

            clonedDictAtomicState.dictionaryState
                = (DictionaryState<AtomicState<TValue>>)dictionaryState.Clone(clonedMap);
        }

        /// <summary>
        /// Locks the dictionary state object.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
            dictionaryState.Lock(visited);
        }

        /// <summary>
        /// Returns a string representation of this dictionary.
        /// </summary>
        /// <returns></returns>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            return dictionaryState.StringRepresentation(objectPaths, path);
        }

        /// <inheritdoc/>
        public void Add(string key, TValue value)
        {
            this[key] = value;
        }

        /// <inheritdoc/>
        public void Add(KeyValuePair<string, TValue> item)
        {
            this[item.Key] = item.Value;
        }

        /// <inheritdoc/>
        public void Clear()
        {
            dictionaryState.Clear();
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            foreach (var key in Keys)
            {
                var value = this[key];

                if (key == item.Key &&
                    Equals(value, item.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }
            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException("The destination array has fewer elements than the collection.");
            }

            var enumerator = GetEnumerator();
            while (enumerator.MoveNext())
            {
                array[arrayIndex++] = enumerator.Current;
            }
        }

        /// <inheritdoc/>
        public bool Remove(KeyValuePair<string, TValue> item)
        {
            foreach (var key in Keys)
            {
                var value = this[key];

                if (key == item.Key &&
                    Equals(value, item.Value))
                {
                    Remove(key);

                    return true;
                }
            }

            return false;
        }
    }
}
