// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// ListState is a collection of state objects. It allows users
    /// to add/remove elements as long as it is not locked.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ListState<T> : State, IList<T>
        where T : State
    {
        private List<T> list { get; set; }

        /// <summary>
        /// Consturcts an instance of this class.
        /// </summary>
        public ListState() : this(null)
        {
        }

        /// <summary>
        /// Creates an instance of ListState given an optional
        /// list of element state objects.
        /// </summary>
        public ListState(List<T> list = null)
        {
            this.list = new List<T>();

            if (list != null)
            {
                foreach (var element in list)
                {
                    this.list.Add(element);
                }
            }
        }

        /// <summary>
        /// Returns the number of elements in the list.
        /// </summary>
        public int Count => list.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => list[index];
            set => InsertAt(index, value);
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return list.GetEnumerator();
        }

        /// <summary>
        /// Adds a state element to the list if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void Add(T item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            list.Add(item);
        }

        /// <summary>
        /// Inserts an element at requested index if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void InsertAt(int index, T item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            list.Insert(index, item);
        }

        /// <summary>
        /// Removes an element at requested index if not locked.
        /// </summary>
        /// <exception cref="StateLockedException"></exception>
        public void RemoveAt(int index)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            list.RemoveAt(index);
        }

        /// <summary>
        /// Deep clones the list, recursively cloning its elements.
        /// </summary>
        /// <returns></returns>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clonedList = new ListState<T>();
            clonedMap[this] = clonedList;

            foreach (var item in list)
            {
                clonedList.Add((T)item?.Clone(clonedMap));
            }
        }

        /// <summary>
        /// Locks each of the element in the list.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
            foreach (var element in list)
            {
                element?.Lock(visited);
            }
        }

        /// <summary>
        /// Returns a string representation of the list.
        /// </summary>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            var elementStrings = new string[list.Count];

            for (int i = 0; i < list.Count; i++)
            {
                var str = elementStrings == null ?
                    "<null>" :
                    list[i].StringRepresentation(objectPaths, path + $"[{i}]");

                elementStrings[i] = str;
            }

            return "[ " + string.Join(", ", elementStrings) + " ]";
        }

        /// <inheritdoc/>
        public int IndexOf(T item)
        {
            return list.IndexOf(item);
        }

        /// <inheritdoc/>
        public void Insert(int index, T item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            InsertAt(index, item);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            list.Clear();
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            return list.Contains(item);

        }

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            list.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc/>
        public bool Remove(T item)
        {
            if (Locked)
            {
                throw new StateLockedException();
            }

            return list.Remove(item);
        }
    }
}
