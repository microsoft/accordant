namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Regression tests asserting that distinct hash-colliding predicates are
    /// NOT merged when combined via boolean operations. Under the EBA-fusion
    /// design, And/Or of two atomic LTL/RLTL formulas pushes the combination
    /// down to the predicate algebra; the responsibility for keeping distinct
    /// operands distinct therefore lives in the EBA's And/Or implementations.
    /// </summary>
    [TestFixture]
    public class PredCompareTests
    {
        // A predicate type whose hashcode is constant: every instance collides.
        private class CollidingProp : IEquatable<CollidingProp>, IComparable<CollidingProp>
        {
            public string Name { get; }
            public CollidingProp(string name) { Name = name; }
            public override int GetHashCode() => 42; // always collides
            public virtual bool Equals(CollidingProp other) =>
                other != null && other.GetType() == GetType() && Name == other.Name;
            public override bool Equals(object obj) => Equals(obj as CollidingProp);
            public virtual int CompareTo(CollidingProp other) =>
                other == null ? 1 : string.Compare(Name, other.Name, StringComparison.Ordinal);
            public override string ToString() => Name;
        }

        private sealed class StructNot : CollidingProp
        {
            public CollidingProp Inner { get; }
            public StructNot(CollidingProp inner) : base("¬" + inner) { Inner = inner; }
            public override bool Equals(CollidingProp other) =>
                other is StructNot n && Inner.Equals(n.Inner);
        }

        private sealed class StructAnd : CollidingProp
        {
            public IReadOnlyList<CollidingProp> Operands { get; }
            public StructAnd(IReadOnlyList<CollidingProp> ops)
                : base("∧(" + string.Join(",", ops) + ")") { Operands = ops; }
            public override bool Equals(CollidingProp other) =>
                other is StructAnd a && a.Operands.SequenceEqual(Operands);
        }

        private sealed class StructOr : CollidingProp
        {
            public IReadOnlyList<CollidingProp> Operands { get; }
            public StructOr(IReadOnlyList<CollidingProp> ops)
                : base("∨(" + string.Join(",", ops) + ")") { Operands = ops; }
            public override bool Equals(CollidingProp other) =>
                other is StructOr o && o.Operands.SequenceEqual(Operands);
        }

        private sealed class CollidingPropEba : IPredicateAlgebra<CollidingProp>
        {
            public CollidingProp Top { get; } = new CollidingProp("⊤");
            public CollidingProp Bottom { get; } = new CollidingProp("⊥");

            public CollidingProp And(CollidingProp a, CollidingProp b)
            {
                var ops = new List<CollidingProp>();
                CollectAnd(a, ops); CollectAnd(b, ops);
                var dedup = Dedup(ops);
                return dedup.Count == 1 ? dedup[0] : new StructAnd(dedup);
            }

            public CollidingProp Or(CollidingProp a, CollidingProp b)
            {
                var ops = new List<CollidingProp>();
                CollectOr(a, ops); CollectOr(b, ops);
                var dedup = Dedup(ops);
                return dedup.Count == 1 ? dedup[0] : new StructOr(dedup);
            }

            public CollidingProp Not(CollidingProp a) =>
                a is StructNot n ? n.Inner : new StructNot(a);

            public bool IsSatisfiable(CollidingProp p) => true;

            private static void CollectAnd(CollidingProp f, List<CollidingProp> acc)
            {
                if (f is StructAnd a) acc.AddRange(a.Operands);
                else acc.Add(f);
            }

            private static void CollectOr(CollidingProp f, List<CollidingProp> acc)
            {
                if (f is StructOr o) acc.AddRange(o.Operands);
                else acc.Add(f);
            }

            private static List<CollidingProp> Dedup(List<CollidingProp> ops)
            {
                var result = new List<CollidingProp>();
                foreach (var o in ops)
                    if (!result.Any(r => r.Equals(o))) result.Add(o);
                result.Sort((x, y) => x.CompareTo(y));
                return result;
            }
        }

        // A predicate type that hash-collides AND does not implement IComparable
        // (so we exercise the ToString tiebreak fallback).
        private class CollidingNoCompare : IEquatable<CollidingNoCompare>
        {
            public string Name { get; }
            public CollidingNoCompare(string name) { Name = name; }
            public override int GetHashCode() => 42;
            public virtual bool Equals(CollidingNoCompare other) =>
                other != null && other.GetType() == GetType() && Name == other.Name;
            public override bool Equals(object obj) => Equals(obj as CollidingNoCompare);
            public override string ToString() => Name;
        }

        private sealed class NCStructOr : CollidingNoCompare
        {
            public IReadOnlyList<CollidingNoCompare> Operands { get; }
            public NCStructOr(IReadOnlyList<CollidingNoCompare> ops)
                : base("∨(" + string.Join(",", ops) + ")") { Operands = ops; }
            public override bool Equals(CollidingNoCompare other) =>
                other is NCStructOr o && o.Operands.SequenceEqual(Operands);
        }

        private sealed class NCStructAnd : CollidingNoCompare
        {
            public IReadOnlyList<CollidingNoCompare> Operands { get; }
            public NCStructAnd(IReadOnlyList<CollidingNoCompare> ops)
                : base("∧(" + string.Join(",", ops) + ")") { Operands = ops; }
            public override bool Equals(CollidingNoCompare other) =>
                other is NCStructAnd a && a.Operands.SequenceEqual(Operands);
        }

        private sealed class NCStructNot : CollidingNoCompare
        {
            public CollidingNoCompare Inner { get; }
            public NCStructNot(CollidingNoCompare inner) : base("¬" + inner) { Inner = inner; }
            public override bool Equals(CollidingNoCompare other) =>
                other is NCStructNot n && Inner.Equals(n.Inner);
        }

        private sealed class CollidingNoCompareEba : IPredicateAlgebra<CollidingNoCompare>
        {
            public CollidingNoCompare Top { get; } = new CollidingNoCompare("⊤");
            public CollidingNoCompare Bottom { get; } = new CollidingNoCompare("⊥");

            public CollidingNoCompare And(CollidingNoCompare a, CollidingNoCompare b)
            {
                var ops = new List<CollidingNoCompare>();
                CollectAnd(a, ops); CollectAnd(b, ops);
                var dedup = Dedup(ops);
                return dedup.Count == 1 ? dedup[0] : new NCStructAnd(dedup);
            }

            public CollidingNoCompare Or(CollidingNoCompare a, CollidingNoCompare b)
            {
                var ops = new List<CollidingNoCompare>();
                CollectOr(a, ops); CollectOr(b, ops);
                var dedup = Dedup(ops);
                return dedup.Count == 1 ? dedup[0] : new NCStructOr(dedup);
            }

            public CollidingNoCompare Not(CollidingNoCompare a) =>
                a is NCStructNot n ? n.Inner : new NCStructNot(a);

            public bool IsSatisfiable(CollidingNoCompare p) => true;

            private static void CollectAnd(CollidingNoCompare f, List<CollidingNoCompare> acc)
            {
                if (f is NCStructAnd a) acc.AddRange(a.Operands);
                else acc.Add(f);
            }

            private static void CollectOr(CollidingNoCompare f, List<CollidingNoCompare> acc)
            {
                if (f is NCStructOr o) acc.AddRange(o.Operands);
                else acc.Add(f);
            }

            private static List<CollidingNoCompare> Dedup(List<CollidingNoCompare> ops)
            {
                var result = new List<CollidingNoCompare>();
                foreach (var o in ops)
                    if (!result.Any(r => r.Equals(o))) result.Add(o);
                // No IComparable on CollidingNoCompare — use ToString tiebreak.
                result.Sort((x, y) => string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal));
                return result;
            }
        }

        [Test]
        public void LtlOr_DistinctHashCollidingAtoms_AreNotMerged()
        {
            var a = new CollidingProp("a");
            var b = new CollidingProp("b");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(b));

            var alg = new LtlAlgebra<CollidingProp>(new CollidingPropEba());
            var or = alg.Or(alg.Atom(a), alg.Atom(b));

            // EBA fuses the two atoms into a single LtlAtom carrying a StructOr predicate.
            Assert.That(or, Is.InstanceOf<LtlAtom<CollidingProp>>());
            var pred = ((LtlAtom<CollidingProp>)or).Predicate;
            Assert.That(pred, Is.InstanceOf<StructOr>());
            Assert.That(((StructOr)pred).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void LtlAnd_DistinctHashCollidingAtoms_AreNotMerged()
        {
            var a = new CollidingProp("a");
            var b = new CollidingProp("b");

            var alg = new LtlAlgebra<CollidingProp>(new CollidingPropEba());
            var and = alg.And(alg.Atom(a), alg.Atom(b));

            Assert.That(and, Is.InstanceOf<LtlAtom<CollidingProp>>());
            var pred = ((LtlAtom<CollidingProp>)and).Predicate;
            Assert.That(pred, Is.InstanceOf<StructAnd>());
            Assert.That(((StructAnd)pred).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void LtlOr_IdempotenceStillHolds_ForEqualPredicates()
        {
            var a1 = new CollidingProp("a");
            var a2 = new CollidingProp("a"); // distinct ref, equal value
            var alg = new LtlAlgebra<CollidingProp>(new CollidingPropEba());
            var or = alg.Or(alg.Atom(a1), alg.Atom(a2));
            // Should collapse to a single atom.
            Assert.That(or, Is.InstanceOf<LtlAtom<CollidingProp>>());
        }

        [Test]
        public void EreUnion_DistinctHashCollidingAtoms_AreNotMerged()
        {
            var a = new CollidingProp("a");
            var b = new CollidingProp("b");

            var u = Ere<CollidingProp>.Union(Ere<CollidingProp>.Atom(a), Ere<CollidingProp>.Atom(b));
            Assert.That(u, Is.InstanceOf<EreUnion<CollidingProp>>());
            Assert.That(((EreUnion<CollidingProp>)u).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void EreIntersect_DistinctHashCollidingAtoms_AreNotMerged()
        {
            var a = new CollidingProp("a");
            var b = new CollidingProp("b");

            var i = Ere<CollidingProp>.Intersect(Ere<CollidingProp>.Atom(a), Ere<CollidingProp>.Atom(b));
            Assert.That(i, Is.InstanceOf<EreIntersect<CollidingProp>>());
            Assert.That(((EreIntersect<CollidingProp>)i).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void RltlOr_DistinctHashCollidingAtoms_AreNotMerged()
        {
            var a = new CollidingProp("a");
            var b = new CollidingProp("b");

            var alg = new RltlAlgebra<CollidingProp>(new CollidingPropEba());
            var or = alg.Or(alg.Atom(a), alg.Atom(b));
            Assert.That(or, Is.InstanceOf<RltlAtom<CollidingProp>>());
            var pred = ((RltlAtom<CollidingProp>)or).Predicate;
            Assert.That(pred, Is.InstanceOf<StructOr>());
            Assert.That(((StructOr)pred).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void NoComparable_HashCollision_StillNotMerged_ViaToStringFallback()
        {
            var a = new CollidingNoCompare("a");
            var b = new CollidingNoCompare("b");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

            var alg = new LtlAlgebra<CollidingNoCompare>(new CollidingNoCompareEba());
            var or = alg.Or(alg.Atom(a), alg.Atom(b));

            Assert.That(or, Is.InstanceOf<LtlAtom<CollidingNoCompare>>());
            var pred = ((LtlAtom<CollidingNoCompare>)or).Predicate;
            Assert.That(pred, Is.InstanceOf<NCStructOr>());
            Assert.That(((NCStructOr)pred).Operands.Count, Is.EqualTo(2));
        }

        [Test]
        public void Ordering_IsConsistentWithEquality()
        {
            // Compare(a,b)==0 iff Equals(a,b) — the contract SortedSet relies on.
            var a1 = Ltl<CollidingProp>.Atom(new CollidingProp("a"));
            var a2 = Ltl<CollidingProp>.Atom(new CollidingProp("a"));
            var b = Ltl<CollidingProp>.Atom(new CollidingProp("b"));

            Assert.That(a1.CompareTo(a2), Is.EqualTo(0));
            Assert.That(a1.Equals(a2), Is.True);

            Assert.That(a1.CompareTo(b), Is.Not.EqualTo(0));
            Assert.That(a1.Equals(b), Is.False);
        }
    }
}
