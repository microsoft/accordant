namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// EREQ Phase-1 (D2) tests: <see cref="Ere{TPred}.FreeProps"/>
    /// metadata defaults to zero for all current node shapes (no
    /// proposition atom exists yet — added in Phase 2), and
    /// <see cref="Ere{TPred}.BitForProp"/> maps negative proposition
    /// indices to the correct bit position.
    /// </summary>
    [TestFixture]
    public class EreFreePropsTests
    {
        private sealed class P
        {
            public string Name { get; }
            public P(string n) { Name = n; }
            public override bool Equals(object obj) => obj is P o && o.Name == Name;
            public override int GetHashCode() => Name.GetHashCode();
            public override string ToString() => Name;
        }

        [Test]
        public void BitForProp_MapsNegativeIndicesToCorrectBit()
        {
            Assert.That(Ere<P>.BitForProp(-1), Is.EqualTo(1UL));
            Assert.That(Ere<P>.BitForProp(-2), Is.EqualTo(2UL));
            Assert.That(Ere<P>.BitForProp(-3), Is.EqualTo(4UL));
            Assert.That(Ere<P>.BitForProp(-64), Is.EqualTo(1UL << 63));
        }

        [Test]
        public void BitForProp_ThrowsOnNonNegativeIndex()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Ere<P>.BitForProp(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Ere<P>.BitForProp(7));
        }

        [Test]
        public void BitForProp_ThrowsBeyondCap()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Ere<P>.BitForProp(-65));
        }

        [Test]
        public void FreeProps_IsZeroForCurrentShapes()
        {
            // Without proposition atoms (added in Phase 2), every term
            // has empty FreeProps. The OR-composition contract is
            // exercised in Phase 2 tests once proposition atoms exist.
            var a = Ere<P>.Atom(new P("a"));
            var b = Ere<P>.Atom(new P("b"));
            Assert.That(Ere<P>.Empty().FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Epsilon().FreeProps, Is.EqualTo(0UL));
            Assert.That(a.FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Concat(a, b).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Union(a, b).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Intersect(a, b).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Complement(a).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Star(a).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Xor(a, b).FreeProps, Is.EqualTo(0UL));
            Assert.That(Ere<P>.Fusion(a, b).FreeProps, Is.EqualTo(0UL));
        }
    }
}
