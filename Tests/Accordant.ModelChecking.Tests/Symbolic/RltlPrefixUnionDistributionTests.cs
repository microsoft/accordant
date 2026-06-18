namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Phase 7 / Layer A — distribution of Union in the regex argument
    /// through the four RLTL <c>Regex × φ</c> prefix operators in
    /// <see cref="Rltl{TPred}"/>:
    ///   <c>(R₁+R₂) ; φ  ≡ (R₁;φ) ∨ (R₂;φ)</c>   (existential)
    ///   <c>(R₁+R₂) : φ  ≡ (R₁:φ) ∨ (R₂:φ)</c>   (existential)
    ///   <c>(R₁+R₂) ⊳ φ ≡ (R₁⊳φ) ∧ (R₂⊳φ)</c>   (universal)
    ///   <c>(R₁+R₂) ⊳⊳ φ ≡ (R₁⊳⊳φ) ∧ (R₂⊳⊳φ)</c> (universal)
    /// </summary>
    [TestFixture]
    public class RltlPrefixUnionDistributionTests
    {
        public sealed class StrPred : IEquatable<StrPred>
        {
            public string Name { get; }
            public StrPred(string n) { Name = n; }
            public bool Equals(StrPred other) => other != null && other.Name == Name;
            public override bool Equals(object o) => Equals(o as StrPred);
            public override int GetHashCode() => Name.GetHashCode();
            public override string ToString() => Name;
        }

        private static Ere<StrPred> EAtom(string n) => Ere<StrPred>.Atom(new StrPred(n));
        private static Rltl<StrPred> RAtom(string n) => Rltl<StrPred>.Atom(new StrPred(n));
        private static Ere<StrPred> EU(params Ere<StrPred>[] ops)
        {
            Ere<StrPred> acc = Ere<StrPred>.Empty();
            foreach (var o in ops) acc = Ere<StrPred>.Union(acc, o);
            return acc;
        }

        [Test]
        public void SeqPrefix_DistributesUnionInRegex_AsOr()
        {
            // (a + b) ; q  ≡  (a;q) ∨ (b;q)
            var phi = RAtom("q");
            var lhs = Rltl<StrPred>.SeqPrefix(EU(EAtom("a"), EAtom("b")), phi);
            var rhs = Rltl<StrPred>.Or(
                Rltl<StrPred>.SeqPrefix(EAtom("a"), phi),
                Rltl<StrPred>.SeqPrefix(EAtom("b"), phi));
            Assert.That(lhs, Is.EqualTo(rhs));
            Assert.That(lhs, Is.InstanceOf<RltlOr<StrPred>>());
        }

        [Test]
        public void OvlPrefix_DistributesUnionInRegex_AsOr()
        {
            var phi = RAtom("q");
            var lhs = Rltl<StrPred>.OvlPrefix(EU(EAtom("a"), EAtom("b")), phi);
            var rhs = Rltl<StrPred>.Or(
                Rltl<StrPred>.OvlPrefix(EAtom("a"), phi),
                Rltl<StrPred>.OvlPrefix(EAtom("b"), phi));
            Assert.That(lhs, Is.EqualTo(rhs));
            Assert.That(lhs, Is.InstanceOf<RltlOr<StrPred>>());
        }

        [Test]
        public void Trigger_DistributesUnionInRegex_AsAnd()
        {
            // (a + b) ⊳ q  ≡  (a⊳q) ∧ (b⊳q)
            var phi = RAtom("q");
            var lhs = Rltl<StrPred>.Trigger(EU(EAtom("a"), EAtom("b")), phi);
            var rhs = Rltl<StrPred>.And(
                Rltl<StrPred>.Trigger(EAtom("a"), phi),
                Rltl<StrPred>.Trigger(EAtom("b"), phi));
            Assert.That(lhs, Is.EqualTo(rhs));
            Assert.That(lhs, Is.InstanceOf<RltlAnd<StrPred>>());
        }

        [Test]
        public void Match_DistributesUnionInRegex_AsAnd()
        {
            var phi = RAtom("q");
            var lhs = Rltl<StrPred>.Match(EU(EAtom("a"), EAtom("b")), phi);
            var rhs = Rltl<StrPred>.And(
                Rltl<StrPred>.Match(EAtom("a"), phi),
                Rltl<StrPred>.Match(EAtom("b"), phi));
            Assert.That(lhs, Is.EqualTo(rhs));
            Assert.That(lhs, Is.InstanceOf<RltlAnd<StrPred>>());
        }

        [Test]
        public void Or_AppliesUnitAndAbsorptionLaws()
        {
            var p = RAtom("p");
            Assert.That(Rltl<StrPred>.Or(Rltl<StrPred>.False(), p), Is.EqualTo(p));
            Assert.That(Rltl<StrPred>.Or(p, Rltl<StrPred>.False()), Is.EqualTo(p));
            Assert.That(Rltl<StrPred>.Or(p, Rltl<StrPred>.True()), Is.InstanceOf<RltlTrue<StrPred>>());
            Assert.That(Rltl<StrPred>.Or(p, p), Is.EqualTo(p), "duplicate operand collapses");
        }

        [Test]
        public void And_AppliesUnitAndAbsorptionLaws()
        {
            var p = RAtom("p");
            Assert.That(Rltl<StrPred>.And(Rltl<StrPred>.True(), p), Is.EqualTo(p));
            Assert.That(Rltl<StrPred>.And(p, Rltl<StrPred>.True()), Is.EqualTo(p));
            Assert.That(Rltl<StrPred>.And(p, Rltl<StrPred>.False()), Is.InstanceOf<RltlFalse<StrPred>>());
            Assert.That(Rltl<StrPred>.And(p, p), Is.EqualTo(p), "duplicate operand collapses");
        }

        [Test]
        public void SeqPrefix_DistributionFlattensNestedUnion()
        {
            // (a + (b + c)) ; q  →  Σ over flattened operands
            var phi = RAtom("q");
            var nested = Ere<StrPred>.Union(EAtom("a"), EU(EAtom("b"), EAtom("c")));
            var lhs = Rltl<StrPred>.SeqPrefix(nested, phi);
            Assert.That(lhs, Is.InstanceOf<RltlOr<StrPred>>());
            var or = (RltlOr<StrPred>)lhs;
            Assert.That(or.Operands.Count, Is.EqualTo(3),
                "nested union flattens through Ere.Union and the Rltl.Or collector");
        }
    }
}
