// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Stack
{
    using System.Collections.Generic;

    public class Stack<T>
    {
        private List<T> list;

        public Stack()
        {
            list = new List<T>();
        }

        public void Push(T item)
        {
            lock (this)
            {
                list.Add(item);
            }
        }

        public T Peek()
        {
            lock (this)
            {
                if (list.Count == 0)
                {
                    throw new EmptyStackException();
                }

                return list[list.Count - 1];
            }
        }

        public T Pop()
        {
            lock (this)
            {
                if (list.Count == 0)
                {
                    throw new EmptyStackException();
                }

                var result = list[list.Count - 1];

                list.RemoveAt(list.Count - 1);

                return result;
            }
        }

        public int Count()
        {
            lock (this)
            {
                return list.Count;
            }
        }

        public bool IsEmpty()
        {
            lock (this)
            {
                return list.Count == 0;
            }
        }
    }
}
