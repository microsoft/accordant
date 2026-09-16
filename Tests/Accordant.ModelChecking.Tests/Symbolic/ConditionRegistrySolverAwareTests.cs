namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for the solver-aware predicate-level union-find in
    /// <see cref="ConditionRegistry{TPredicate}"/>. When an
    /// <see cref="IPredicateAlgebra{T}"/> is provided to the registry,
    /// structurally-distinct predicates that are reported equivalent by
    /// <see cref="EbaExtensions.AreEquivalent{T}"/> are aliased to a single
    /// condition index — the predicate-level analogue of the regex-level
    /// canonicalisation in <see cref="EreCanonicalizer{TPred,TElem}"/>.
    /// </summary>
    [TestFixture]
    public class ConditionRegistrySolverAwareTests
    {
        /// <summary>
        /// Predicate with reference-only equality, named for debugging.
        /// Structurally distinct instances always compare unequal even when
        /// they carry the same set of integers; the only way to alias them
        /// is via the EBA's <see cref="IPredicateAlgebraEx{T}.AreEquivalent"/>.
        /// </summary>
        private sealed class OpaquePredicate
        {
            public string Name { get; }
            public HashSet<int> Elements { get; }
            public OpaquePredicate(string name, params int[] elements)
            {
                Name = name;
                Elements = new HashSet<int>(elements);
            }
            public override string ToString() => Name;
            // Default Equals/GetHashCode = reference-based: distinct
            // instances never compare structurally equal.
        }

        private sealed class OpaqueEba : IPredicateAlgebraEx<OpaquePredicate>
        {
            public OpaquePredicate Top { get; } = new OpaquePredicate("⊤", 0, 1, 2);
            public OpaquePredicate Bottom { get; } = new OpaquePredicate("⊥");

            public OpaquePredicate And(OpaquePredicate a, OpaquePredicate b)
            {
                var set = new HashSet<int>(a.Elements); set.IntersectWith(b.Elements);
                var arr = new int[set.Count]; int k = 0;
                foreach (var e in set) arr[k++] = e;
                return new OpaquePredicate($"({a.Name}∧{b.Name})", arr);
            }
            public OpaquePredicate Or(OpaquePredicate a, OpaquePredicate b)
            {
                var set = new HashSet<int>(a.Elements); set.UnionWith(b.Elements);
                var arr = new int[set.Count]; int k = 0;
                foreach (var e in set) arr[k++] = e;
                return new OpaquePredicate($"({a.Name}∨{b.Name})", arr);
            }
            public OpaquePredicate Not(OpaquePredicate a)
            {
                var set = new HashSet<int> { 0, 1, 2 }; set.ExceptWith(a.Elements);
                var arr = new int[set.Count]; int k = 0;
                foreach (var e in set) arr[k++] = e;
                return new OpaquePredicate($"¬{a.Name}", arr);
            }
            public bool IsSatisfiable(OpaquePredicate p) => p.Elements.Count > 0;
            public bool AreEquivalent(OpaquePredicate a, OpaquePredicate b)
                => a.Elements.SetEquals(b.Elements);
            public bool Implies(OpaquePredicate a, OpaquePredicate b)
                => a.Elements.IsSubsetOf(b.Elements);
        }

        [Test]
        public void Plain_Registry_DoesNotAliasOpaquePredicates()
        {
            var r = new ConditionRegistry<OpaquePredicate>();
            var p1 = new OpaquePredicate("p1", 0, 1);
            var p2 = new OpaquePredicate("p2", 0, 1); // same elements, different instance
            var i1 = r.Register(p1);
            var i2 = r.Register(p2);
            Assert.That(i2, Is.Not.EqualTo(i1));
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.SolverAliasCount, Is.EqualTo(0));
        }

        [Test]
        public void SolverAware_Registry_AliasesEquivalentPredicates()
        {
            var eba = new OpaqueEba();
            var r = new ConditionRegistry<OpaquePredicate>(null, eba);
            var p1 = new OpaquePredicate("p1", 0, 1);
            var p2 = new OpaquePredicate("p2", 0, 1); // semantically equal to p1
            var p3 = new OpaquePredicate("p3", 0, 2); // distinct

            var i1 = r.Register(p1);
            var i2 = r.Register(p2);
            var i3 = r.Register(p3);

            Assert.That(i2, Is.EqualTo(i1), "p1 and p2 must alias to a single index.");
            Assert.That(i3, Is.Not.EqualTo(i1));
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.SolverAliasCount, Is.EqualTo(1));
        }

        [Test]
        public void SolverAware_Registry_RepeatedQueries_AreO1Cached()
        {
            var eba = new OpaqueEba();
            var r = new ConditionRegistry<OpaquePredicate>(null, eba);
            var p1 = new OpaquePredicate("p1", 0);
            var p2 = new OpaquePredicate("p2", 0);

            var i1a = r.Register(p1);
            var i1b = r.Register(p1);
            var i2a = r.Register(p2);
            var i2b = r.Register(p2);

            Assert.That(i1a, Is.EqualTo(i1b));
            Assert.That(i2a, Is.EqualTo(i2b));
            Assert.That(i1a, Is.EqualTo(i2a));
            // Only one solver-aware alias was recorded (the first time p2
            // was registered); subsequent p2 calls hit the structural cache.
            Assert.That(r.SolverAliasCount, Is.EqualTo(1));
        }
    }
}
