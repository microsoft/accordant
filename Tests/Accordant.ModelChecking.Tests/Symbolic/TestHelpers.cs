namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// A simple integer predicate for testing: predicates are sets of integers.
    /// The universe is a finite set of integers {0..N-1}.
    /// </summary>
    public class IntPredicate : IEquatable<IntPredicate>
    {
        public HashSet<int> Elements { get; }
        public string Name { get; }

        public IntPredicate(string name, params int[] elements)
        {
            Name = name;
            Elements = new HashSet<int>(elements);
        }

        public IntPredicate(string name, IEnumerable<int> elements)
        {
            Name = name;
            Elements = new HashSet<int>(elements);
        }

        public bool Equals(IntPredicate other)
            => other != null && Elements.SetEquals(other.Elements);

        public override bool Equals(object obj) => Equals(obj as IntPredicate);
        public override int GetHashCode()
        {
            int h = 0;
            foreach (var e in Elements.OrderBy(x => x))
                h = h * 31 + e;
            return h;
        }

        public override string ToString() => Name ?? $"{{{string.Join(",", Elements.OrderBy(x => x))}}}";
    }

    /// <summary>
    /// EBA over integers with a finite universe {0..Size-1}.
    /// Predicates are sets of integers; satisfiability = non-empty set.
    /// </summary>
    public class IntEba : IEffectiveBooleanAlgebraEx<IntPredicate, int>
    {
        private readonly int _size;

        public IntEba(int size)
        {
            _size = size;
            Top = new IntPredicate("⊤", Enumerable.Range(0, size));
            Bottom = new IntPredicate("⊥");
        }

        public IntPredicate Top { get; }
        public IntPredicate Bottom { get; }

        public IntPredicate And(IntPredicate a, IntPredicate b)
        {
            var intersection = new HashSet<int>(a.Elements);
            intersection.IntersectWith(b.Elements);
            return new IntPredicate($"({a.Name}∧{b.Name})", intersection);
        }

        public IntPredicate Or(IntPredicate a, IntPredicate b)
        {
            var union = new HashSet<int>(a.Elements);
            union.UnionWith(b.Elements);
            return new IntPredicate($"({a.Name}∨{b.Name})", union);
        }

        public IntPredicate Not(IntPredicate a)
        {
            var complement = Enumerable.Range(0, _size).Where(i => !a.Elements.Contains(i));
            return new IntPredicate($"¬{a.Name}", complement);
        }

        public bool IsSatisfiable(IntPredicate predicate) => predicate.Elements.Count > 0;

        public bool Models(int element, IntPredicate predicate) => predicate.Elements.Contains(element);

        public bool AreEquivalent(IntPredicate a, IntPredicate b)
            => a.Elements.SetEquals(b.Elements);

        public bool Implies(IntPredicate a, IntPredicate b)
            => a.Elements.IsSubsetOf(b.Elements);

        public bool TryGetModel(IntPredicate predicate, out int element)
        {
            foreach (var e in predicate.Elements) { element = e; return true; }
            element = default;
            return false;
        }
    }

    /// <summary>
    /// Simple string-based leaf algebra for testing.
    /// Leaves are strings; Or/And produce ACI-normalized sorted comma-separated forms.
    /// </summary>
    public class StringLeafAlgebra : ILeafAlgebra<string>
    {
        public string Top => "⊤";
        public string Bottom => "⊥";

        public bool IsTop(string a) => a == "⊤";
        public bool IsBottom(string a) => a == "⊥";

        public string Or(string a, string b)
        {
            if (IsTop(a) || IsTop(b)) return Top;
            if (IsBottom(a)) return b;
            if (IsBottom(b)) return a;
            if (a == b) return a; // idempotent

            // ACI: parse, merge, sort, deduplicate
            var parts = ParseDisjuncts(a).Union(ParseDisjuncts(b)).OrderBy(x => x).ToList();
            return parts.Count == 1 ? parts[0] : string.Join("∨", parts);
        }

        public string And(string a, string b)
        {
            if (IsBottom(a) || IsBottom(b)) return Bottom;
            if (IsTop(a)) return b;
            if (IsTop(b)) return a;
            if (a == b) return a; // idempotent

            // ACI: parse, merge, sort, deduplicate
            var parts = ParseConjuncts(a).Union(ParseConjuncts(b)).OrderBy(x => x).ToList();
            return parts.Count == 1 ? parts[0] : string.Join("∧", parts);
        }

        public string Not(string a)
        {
            if (IsTop(a)) return Bottom;
            if (IsBottom(a)) return Top;
            return $"¬{a}";
        }

        public string Xor(string a, string b)
        {
            // Fallback: (a ∧ ¬b) ∨ (¬a ∧ b)
            return Or(And(a, Not(b)), And(Not(a), b));
        }

        public IEqualityComparer<string> Comparer => StringComparer.Ordinal;

        private static IEnumerable<string> ParseDisjuncts(string s)
            => s.Contains("∨") ? s.Split(new[] { '∨' }) : new[] { s };

        private static IEnumerable<string> ParseConjuncts(string s)
            => s.Contains("∧") ? s.Split(new[] { '∧' }) : new[] { s };
    }
}
