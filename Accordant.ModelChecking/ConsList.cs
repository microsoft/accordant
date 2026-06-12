namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// Immutable singly-linked list with structural sharing — used as a
    /// cheap "backwards-witness" accumulator during regex emptiness /
    /// equivalence checks. Cons is O(1); enumeration walks head-to-tail.
    ///
    /// Conventions:
    ///  * <see cref="Empty"/> is a shared singleton representing Nil.
    ///  * <c>Cons(head, tail)</c> prepends a new head.
    ///  * For symbolic witnesses we build the list in reverse (most recent
    ///    condition at the head); callers reverse on extraction.
    /// </summary>
    public sealed class ConsList<T> : IEnumerable<T>
    {
        public static readonly ConsList<T> Empty = new ConsList<T>();

        public T Head { get; }
        public ConsList<T> Tail { get; }
        public int Count { get; }
        public bool IsEmpty => Count == 0;

        private ConsList()
        {
            Head = default;
            Tail = null;
            Count = 0;
        }

        private ConsList(T head, ConsList<T> tail)
        {
            Head = head;
            Tail = tail;
            Count = tail.Count + 1;
        }

        public static ConsList<T> Cons(T head, ConsList<T> tail)
        {
            if (tail == null) throw new ArgumentNullException(nameof(tail));
            return new ConsList<T>(head, tail);
        }

        public ConsList<T> Push(T head) => new ConsList<T>(head, this);

        /// <summary>Reverses the list (head becomes tail).</summary>
        public ConsList<T> Reverse()
        {
            var acc = Empty;
            for (var node = this; !node.IsEmpty; node = node.Tail)
            {
                acc = new ConsList<T>(node.Head, acc);
            }
            return acc;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var node = this; !node.IsEmpty; node = node.Tail)
            {
                yield return node.Head;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static ConsList<T> FromEnumerableReversed(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var acc = Empty;
            foreach (var x in items) acc = new ConsList<T>(x, acc);
            return acc;
        }
    }
}
