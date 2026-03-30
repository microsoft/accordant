// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// ListAtomicState is a collection of objects internally wrapped in 
    /// AtomicState objects. It allows users to add/remove elements as long
    /// as it is not locked.
    /// </summary>
    public class ListAtomicState<T> : State, IList<T>
    {
        private ListState<AtomicState<T>> listState = new ListState<AtomicState<T>>();

        /// <summary>
        /// Consturcts an instance of this class.
        /// </summary>
        public ListAtomicState() : this(null)
        {
        }

        /// <summary>
        /// Constructs an instance of ListAtomicState given an optional list
        /// of objects (wraps each object inside AtomicState to make the value atomic).
        /// </summary>
        public ListAtomicState(List<T> list = null)
        {
            listState = new ListState<AtomicState<T>>();

            if (list != null)
            {
                foreach (T element in list)
                {
                    listState.Add(new AtomicState<T>(element));
                }
            }
        }

        /// <summary>
        /// Returns the number of elements in the list.
        /// </summary>
        public int Count => listState.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => listState[index].Value;
            set => listState[index] = new AtomicState<T>(value);
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            return listState.Select(e => e.Value).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return listState.Select(e => e.Value).GetEnumerator();
        }

        /// <summary>
        /// Adds an element to the list if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void Add(T item)
        {
            listState.Add(new AtomicState<T>(item));
        }

        /// <summary>
        /// Inserts an element at requested index if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void InsertAt(int index, T item)
        {
            listState.InsertAt(index, new AtomicState<T>(item));
        }

        /// <summary>
        /// Removes an element at requested index if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void RemoveAt(int index)
        {
            listState.RemoveAt(index);
        }

        /// <summary>
        /// Deep clones the list, recursively cloning its elements.
        /// </summary>
        /// <returns></returns>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clonedListAtomicState = new ListAtomicState<T>();
            clonedMap[this] = clonedListAtomicState;

            clonedListAtomicState.listState =
                (ListState<AtomicState<T>>)listState.Clone(clonedMap);
        }

        /// <summary>
        /// Locks the underlying list state object.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
            listState.Lock(visited);
        }

        /// <summary>
        /// Returns a string representation of the list.
        /// </summary>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            return listState.StringRepresentation(objectPaths, path);
        }

        /// <inheritdoc/>
        public int IndexOf(T item)
        {
            for (int i = 0; i < listState.Count; i++)
            {
                if (Equals(listState[i].Value, item))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <inheritdoc/>
        public void Insert(int index, T item)
        {
            InsertAt(index, item);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            listState.Clear();
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            return listState.Any(i => Equals(i.Value, item));
        }

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            }
            if (array.Length - arrayIndex < listState.Count)
            {
                throw new ArgumentException("The destination array has fewer elements than the collection.");
            }

            for (int i = 0; i < listState.Count; i++)
            {
                array[arrayIndex + i] = listState[i].Value;
            }
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            for (int i = 0; i < listState.Count; i++)
            {
                if (Equals(listState[i].Value, item))
                {
                    listState.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }
}
