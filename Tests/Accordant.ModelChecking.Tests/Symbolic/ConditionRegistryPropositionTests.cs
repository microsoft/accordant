namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// EREQ Phase-1 tests: proposition support in
    /// <see cref="ConditionRegistry{TPredicate}"/>. Propositions live
    /// in a separate index range (strictly negative) from predicates
    /// (non-negative), so the two streams never collide.
    /// </summary>
    [TestFixture]
    public class ConditionRegistryPropositionTests
    {
        private sealed class P
        {
            public string Name { get; }
            public P(string name) { Name = name; }
            public override bool Equals(object obj) => obj is P o && o.Name == Name;
            public override int GetHashCode() => Name.GetHashCode();
            public override string ToString() => Name;
        }

        [Test]
        public void RegisterProposition_AllocatesNegativeIndicesInOrder()
        {
            var r = new ConditionRegistry<P>();
            Assert.That(r.RegisterProposition("p"), Is.EqualTo(-1));
            Assert.That(r.RegisterProposition("q"), Is.EqualTo(-2));
            Assert.That(r.RegisterProposition("s"), Is.EqualTo(-3));
            Assert.That(r.PropositionCount, Is.EqualTo(3));
        }

        [Test]
        public void RegisterProposition_IsIdempotentOnRepeatedName()
        {
            var r = new ConditionRegistry<P>();
            var first = r.RegisterProposition("p");
            var again = r.RegisterProposition("p");
            Assert.That(again, Is.EqualTo(first));
            Assert.That(r.PropositionCount, Is.EqualTo(1));
        }

        [Test]
        public void PredicateAndPropositionStreamsAreDisjoint()
        {
            var r = new ConditionRegistry<P>();
            var pred0 = r.Register(new P("a"));
            var prop0 = r.RegisterProposition("p");
            var pred1 = r.Register(new P("b"));
            var prop1 = r.RegisterProposition("q");

            Assert.That(pred0, Is.EqualTo(0));
            Assert.That(pred1, Is.EqualTo(1));
            Assert.That(prop0, Is.EqualTo(-1));
            Assert.That(prop1, Is.EqualTo(-2));

            Assert.That(ConditionRegistry<P>.IsProposition(pred0), Is.False);
            Assert.That(ConditionRegistry<P>.IsProposition(pred1), Is.False);
            Assert.That(ConditionRegistry<P>.IsProposition(prop0), Is.True);
            Assert.That(ConditionRegistry<P>.IsProposition(prop1), Is.True);
        }

        [Test]
        public void GetPropositionName_RoundTripsRegisteredNames()
        {
            var r = new ConditionRegistry<P>();
            var pIdx = r.RegisterProposition("p");
            var qIdx = r.RegisterProposition("q");
            Assert.That(r.GetPropositionName(pIdx), Is.EqualTo("p"));
            Assert.That(r.GetPropositionName(qIdx), Is.EqualTo("q"));
        }

        [Test]
        public void GetPropositionName_ThrowsOnNonNegativeIndex()
        {
            var r = new ConditionRegistry<P>();
            r.Register(new P("a"));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.GetPropositionName(0));
        }

        [Test]
        public void IndexOfProposition_ReturnsZeroSentinelWhenNotRegistered()
        {
            var r = new ConditionRegistry<P>();
            r.RegisterProposition("p");
            Assert.That(r.IndexOfProposition("p"), Is.EqualTo(-1));
            Assert.That(r.IndexOfProposition("nope"), Is.EqualTo(0));
        }

        [Test]
        public void RegisterProposition_ThrowsOverMaxPropositions()
        {
            var r = new ConditionRegistry<P>();
            for (int i = 0; i < ConditionRegistry<P>.MaxPropositions; i++)
                r.RegisterProposition("p" + i);
            Assert.Throws<InvalidOperationException>(() => r.RegisterProposition("overflow"));
        }

        [Test]
        public void IsProposition_StaticHelperMatchesSign()
        {
            Assert.That(ConditionRegistry<P>.IsProposition(-1), Is.True);
            Assert.That(ConditionRegistry<P>.IsProposition(-64), Is.True);
            Assert.That(ConditionRegistry<P>.IsProposition(0), Is.False);
            Assert.That(ConditionRegistry<P>.IsProposition(7), Is.False);
        }

        [Test]
        public void Propositions_EnumerationOrderMatchesRegistration()
        {
            var r = new ConditionRegistry<P>();
            r.RegisterProposition("p");
            r.RegisterProposition("q");
            r.RegisterProposition("s");
            Assert.That(r.Propositions, Is.EqualTo(new[] { "p", "q", "s" }));
        }
    }
}
