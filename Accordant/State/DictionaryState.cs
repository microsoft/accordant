// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;

namespace Microsoft.Accordant
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// DictionaryState is an object mapping strings to state objects.
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    public class DictionaryState<TValue> : State, IDictionary<string, TValue>
        where TValue : State
    {
        private Dictionary<string, TValue> dict { get; set; }

        /// <summary>
        /// Consturcts an instance of this class.
        /// </summary>
        public DictionaryState() : this(null)
        {
        }

        /// <summary>
        /// Constructs an instance of DictionaryState given an optional dictionary
        /// mapping strings to state objects.
        /// </summary>
        /// <param name="dict"></param>
        public DictionaryState(IReadOnlyDictionary<string, TValue> dict = null)
        {
            this.dict = new Dictionary<string, TValue>();

            if (dict != null)
            {
                foreach (var key in dict.Keys)
                {
                    this.dict[key] = dict[key];
                }
            }
        }

        /// <summary>
        /// Gets the state object given key. Sets (create or update) a state object
        /// with the given key if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public TValue this[string key]
        {
            get => dict[key];
            set
            {
                if (Locked)
                {
                    throw new StateLockedException();
                }

                dict[key] = value;
            }
        }

        /// <summary>
        /// Returns the number of key/value pairs in the dictionary.
        /// </summary>
        public int Count => dict.Count;

        /// <inheritdoc/>
        public ICollection<string> Keys => dict.Keys;

        /// <inheritdoc/>
        public ICollection<TValue> Values => dict.Values;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <summary>
        /// Indicates whether the dictionary contains the given key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool ContainsKey(string key)
        {
            return dict.ContainsKey(key);
        }

        /// <summary>
        /// Attempts to fetch the state object if the given key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public bool TryGetValue(string key, out TValue value)
        {
            return dict.TryGetValue(key, out value);
        }

        /// <summary>
        /// Returns an enumerator for the key/value pairs in this object.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            return dict.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator for the key/value pairs in this object.
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }

        /// <summary>
        /// Removes the given key from the dictionary if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public bool Remove(string key)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            return dict.Remove(key);
        }

        /// <summary>
        /// Deep clones this dictionary by cloning all the state object values.
        /// </summary>
        /// <returns></returns>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clonedDict = new DictionaryState<TValue>();
            clonedMap[this] = clonedDict;

            foreach (var k in dict.Keys)
            {
                clonedDict[k] = (TValue)dict[k]?.Clone(clonedMap);
            }
        }

        /// <summary>
        /// Locks the state object values in this dictionary.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
            foreach (var key in dict.Keys)
            {
                dict[key]?.Lock(visited);
            }
        }

        /// <summary>
        /// Returns a string representation of this dictionary.
        /// </summary>
        /// <returns></returns>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            var keyValueStrings = new List<string>();
            foreach (var key in dict.Keys)
            {
                var val = dict[key];

                var valStr = val == null ?
                    "<null>" :
                    val.StringRepresentation(objectPaths, path + "." + key);

                keyValueStrings.Add($"{key}: {valStr}");
            }

            return "{ " + string.Join(", ", keyValueStrings) + " }";
        }

        /// <inheritdoc/>
        public void Add(string key, TValue value)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            this[key] = value;
        }

        public void Add(KeyValuePair<string, TValue> item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            this[item.Key] = item.Value;
        }

        public void Clear()
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            dict.Clear();
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            return dict.Contains(item);
        }

        public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, TValue>>)dict).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<string, TValue> item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            return ((ICollection<KeyValuePair<string, TValue>>)dict).Remove(item);
        }
    }
}
