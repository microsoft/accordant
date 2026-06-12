namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class EreWitnessTests
    {
        private sealed class Prop : IEquatable<Prop>, IComparable<Prop>
        {
            public string Name { get; }
            public Prop(string name) { Name = name; }
            public bool Equals(Prop other) => other != null && Name == other.Name;
            public override bool Equals(object obj) => Equals(obj as Prop);
            public override int GetHashCode() => Name.GetHashCode();
            public int CompareTo(Prop other) => string.Compare(Name, other?.Name, StringComparison.Ordinal);
            public override string ToString() => Name;
        }

        private sealed class PropEba : IEffectiveBooleanAlgebra<Prop, HashSet<string>>
        {
            public Prop Top { get; } = new Prop("⊤");
            public Prop Bottom { get; } = new Prop("⊥");
            public Prop And(Prop a, Prop b)
            {
                if (a.Name == "⊤") return b;
                if (b.Name == "⊤") return a;
                if (a.Name == "⊥" || b.Name == "⊥") return Bottom;
                if (a.Equals(b)) return a;
                return new Prop($"({a.Name}∧{b.Name})");
            }
            public Prop Or(Prop a, Prop b)
            {
                if (a.Name == "⊥") return b;
                if (b.Name == "⊥") return a;
                if (a.Name == "⊤" || b.Name == "⊤") return Top;
                if (a.Equals(b)) return a;
                return new Prop($"({a.Name}∨{b.Name})");
            }
            public Prop Not(Prop a)
            {
                if (a.Name == "⊤") return Bottom;
                if (a.Name == "⊥") return Top;
                if (a.Name.StartsWith("¬")) return new Prop(a.Name.Substring(1));
                return new Prop($"¬{a.Name}");
            }
            public bool IsSatisfiable(Prop p) => p.Name != "⊥";
            public bool Models(HashSet<string> e, Prop p)
            {
                if (p.Name == "⊤") return true;
                if (p.Name == "⊥") return false;
                if (p.Name.StartsWith("¬")) return !e.Contains(p.Name.Substring(1));
                return e.Contains(p.Name);
            }
        }

        private static Ere<Prop> Atom(string n) => Ere<Prop>.Atom(new Prop(n));

        private static (EreEquivalenceChecker<Prop, HashSet<string>> equiv,
                       EreEmptinessChecker<Prop, HashSet<string>> empt,
                       PropEba eba) MakeCheckers()
        {
            var eba = new PropEba();
            var reg = new ConditionRegistry<Prop>();
            var deriv = new EreDerivative<Prop, HashSet<string>>(eba, reg);
            var empt = new EreEmptinessChecker<Prop, HashSet<string>>(deriv);
            var equiv = new EreEquivalenceChecker<Prop, HashSet<string>>(deriv, empt);
            return (equiv, empt, eba);
        }

        // Materialise a positive-literal predicate by picking the obvious
        // singleton; ⊤ and ¬x pick the empty set.
        private static HashSet<string> Pick(Prop p)
        {
            var n = p.Name;
            if (n == "⊤") return new HashSet<string>();
            if (n == "⊥") throw new InvalidOperationException("unsat");
            if (n.StartsWith("¬")) return new HashSet<string>();
            if (n.StartsWith("(") && n.Contains("∧"))
            {
                // very simple conjunction parser: split top-level by ∧.
                var inner = n.Substring(1, n.Length - 2);
                var set = new HashSet<string>();
                foreach (var part in inner.Split('∧'))
                    if (!part.StartsWith("¬")) set.Add(part);
                return set;
            }
            return new HashSet<string> { n };
        }

        // --- ConsList witness shape ---

        [Test]
        public void Empty_NonEmptyEpsilon_EmptyWitness()
        {
            var (_, empt, _) = MakeCheckers();
            Assert.That(empt.NonEmpty(Ere<Prop>.Epsilon(), ConsList<Prop>.Empty,
                out var w), Is.True);
            Assert.That(w.IsEmpty, Is.True);
        }

        [Test]
        public void Empty_DeadReturnsFalse()
        {
            var (_, empt, _) = MakeCheckers();
            Assert.That(empt.NonEmpty(Ere<Prop>.Empty(), ConsList<Prop>.Empty,
                out var w), Is.False);
            Assert.That(w, Is.Null);
        }

        [Test]
        public void Empty_SingleAtomWitnessLength1()
        {
            var (_, empt, _) = MakeCheckers();
            Assert.That(empt.NonEmpty(Atom("a"), ConsList<Prop>.Empty,
                out var w), Is.True);
            Assert.That(w.Count, Is.EqualTo(1));
            Assert.That(w.Head.Name, Does.Contain("a"));
        }

        [Test]
        public void Empty_ConcatWitnessLength3()
        {
            // a·b·c — accepted word has 3 symbols matching a, b, c respectively.
            var (_, empt, _) = MakeCheckers();
            var r = Ere<Prop>.Concat(Atom("a"), Ere<Prop>.Concat(Atom("b"), Atom("c")));
            Assert.That(empt.NonEmpty(r, ConsList<Prop>.Empty, out var w), Is.True);
            Assert.That(w.Count, Is.EqualTo(3));
            // Forward order via Reverse: a, b, c.
            var fwd = w.Reverse().ToList();
            Assert.That(fwd[0].Name, Does.Contain("a"));
            Assert.That(fwd[1].Name, Does.Contain("b"));
            Assert.That(fwd[2].Name, Does.Contain("c"));
        }

        [Test]
        public void Empty_PrefixThreaded()
        {
            // NonEmpty with a non-empty prefix appends (in reverse) onto the
            // new witness — prefix should remain as the tail.
            var (_, empt, _) = MakeCheckers();
            var prefix = ConsList<Prop>.Empty.Push(new Prop("x"));
            Assert.That(empt.NonEmpty(Atom("a"), prefix, out var w), Is.True);
            Assert.That(w.Count, Is.EqualTo(2));
            // Head = most recent (a), tail = prefix (x).
            Assert.That(w.Head.Name, Does.Contain("a"));
            Assert.That(w.Tail.Head.Name, Is.EqualTo("x"));
        }

        // --- Equivalence-checker witness ---

        [Test]
        public void Inequivalent_DistinctAtoms()
        {
            var (eq, _, eba) = MakeCheckers();
            Assert.That(eq.AreInequivalent(Atom("a"), Atom("b"), out var w), Is.True);
            // Witness is a 1-letter word distinguishing the two.
            Assert.That(w.Count, Is.EqualTo(1));
            var elem = Pick(w.Head);
            // The element satisfies exactly one of a, b.
            Assert.That(eba.Models(elem, new Prop("a")) ^ eba.Models(elem, new Prop("b")),
                Is.True);
        }

        [Test]
        public void Inequivalent_SubsumptionPvsPunionQ()
        {
            // a ≢ a + b  — witness should be in L(b) \ L(a).
            var (eq, _, eba) = MakeCheckers();
            var pq = Ere<Prop>.Union(Atom("a"), Atom("b"));
            Assert.That(eq.AreInequivalent(Atom("a"), pq, out var w), Is.True);
            Assert.That(w.Count, Is.EqualTo(1));
            // The witness symbol must NOT satisfy a (else accepted by both);
            // it must satisfy b (else accepted by neither).
            var elem = Pick(w.Head);
            Assert.That(eba.Models(elem, new Prop("a")), Is.False);
            Assert.That(eba.Models(elem, new Prop("b")), Is.True);
        }

        [Test]
        public void Inequivalent_StarVsDoubleStar()
        {
            // a*  ≢  (a·a)*   — witness must be an odd-length a^k word
            // accepted by a* but not (a·a)*.
            var (eq, _, eba) = MakeCheckers();
            var aStar = Ere<Prop>.Star(Atom("a"));
            var aaStar = Ere<Prop>.Star(Ere<Prop>.Concat(Atom("a"), Atom("a")));
            Assert.That(eq.AreInequivalent(aStar, aaStar, out var w), Is.True);
            Assert.That(w.Count % 2, Is.EqualTo(1), "witness length must be odd");
            // Every symbol must satisfy a.
            foreach (var p in w)
            {
                Assert.That(eba.Models(Pick(p), new Prop("a")), Is.True);
            }
        }

        [Test]
        public void Equivalent_NoWitness()
        {
            var (eq, _, _) = MakeCheckers();
            Assert.That(eq.AreInequivalent(Atom("a"), Atom("a"), out var w), Is.False);
            Assert.That(w, Is.Null);
        }

        [Test]
        public void Equivalent_AciNoWitness()
        {
            var (eq, _, _) = MakeCheckers();
            var l = Ere<Prop>.Union(Atom("a"), Atom("b"));
            var r = Ere<Prop>.Union(Atom("b"), Atom("a"));
            Assert.That(eq.AreInequivalent(l, r, out var w), Is.False);
            Assert.That(w, Is.Null);
        }

        // --- Materialisation helper ---

        [Test]
        public void Materialise_ForwardOrder()
        {
            var (_, empt, _) = MakeCheckers();
            var r = Ere<Prop>.Concat(Atom("a"), Atom("b"));
            Assert.That(empt.NonEmpty(r, ConsList<Prop>.Empty, out var w), Is.True);
            var elems = EreWitness.Materialise(w, Pick);
            Assert.That(elems.Count, Is.EqualTo(2));
            Assert.That(elems[0], Does.Contain("a"));
            Assert.That(elems[1], Does.Contain("b"));
        }

        [Test]
        public void Materialise_EmptyOrNull()
        {
            Assert.That(EreWitness.ToForward((ConsList<Prop>)null), Is.Empty);
            Assert.That(EreWitness.ToForward(ConsList<Prop>.Empty), Is.Empty);
        }
    }
}
