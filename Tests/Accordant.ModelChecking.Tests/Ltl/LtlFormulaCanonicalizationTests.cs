namespace Accordant.ModelChecking.Tests.Ltl
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Ltl;
    using NUnit.Framework;

    [TestFixture]
    public class LtlFormulaCanonicalizationTests
    {
        private static LtlFormula P(string name) =>
            // Each call creates a distinct closure capturing `name`, so two
            // Props with different names have distinct Predicate references
            // (LtlProp equality is ReferenceEquals on the predicate delegate).
            LtlFormula.Prop(s => name != null, name);

        [Test]
        public void And_Idempotent_CollapsesDuplicates()
        {
            var p = P("p");
            var q = P("q");
            var a = LtlFormula.And(p, p);
            var b = LtlFormula.And(LtlFormula.And(p, q), p);
            Assert.That(a, Is.EqualTo(p));
            Assert.That(b, Is.EqualTo(LtlFormula.And(p, q)));
        }

        [Test]
        public void And_Commutative_ReordersToCanonical()
        {
            var p = P("p");
            var q = P("q");
            var a = LtlFormula.And(p, q);
            var b = LtlFormula.And(q, p);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo(b.ToString()));
        }

        [Test]
        public void And_Associative_FlattensNestedAnds()
        {
            var p = P("p"); var q = P("q"); var r = P("r");
            var a = LtlFormula.And(p, LtlFormula.And(q, r));
            var b = LtlFormula.And(LtlFormula.And(p, q), r);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Or_Idempotent_Commutative_Associative()
        {
            var p = P("p"); var q = P("q"); var r = P("r");
            var a = LtlFormula.Or(p, LtlFormula.Or(q, r));
            var b = LtlFormula.Or(LtlFormula.Or(r, q), p);
            var c = LtlFormula.Or(LtlFormula.Or(p, q), LtlFormula.Or(r, p));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.EqualTo(c));
        }

        [Test]
        public void DoubleNegation_Eliminated()
        {
            var p = P("p");
            var notNotP = LtlFormula.Not(LtlFormula.Not(p));
            Assert.That(notNotP, Is.EqualTo(p));
        }

        [Test]
        public void DeMorgan_PushedThroughAnd()
        {
            var p = P("p");
            var q = P("q");
            var lhs = LtlFormula.Not(LtlFormula.And(p, q));
            var rhs = LtlFormula.Or(LtlFormula.Not(p), LtlFormula.Not(q));
            Assert.That(lhs, Is.EqualTo(rhs));
        }

        [Test]
        public void DeMorgan_PushedThroughOr()
        {
            var p = P("p");
            var q = P("q");
            var lhs = LtlFormula.Not(LtlFormula.Or(p, q));
            var rhs = LtlFormula.And(LtlFormula.Not(p), LtlFormula.Not(q));
            Assert.That(lhs, Is.EqualTo(rhs));
        }

        [Test]
        public void Negation_PushedThroughNext()
        {
            var p = P("p");
            var lhs = LtlFormula.Not(LtlFormula.Next(p));
            var rhs = LtlFormula.Next(LtlFormula.Not(p));
            Assert.That(lhs, Is.EqualTo(rhs));
        }

        [Test]
        public void Negation_UntilReleaseDuality()
        {
            var p = P("p");
            var q = P("q");
            // ¬(p U q) = (¬p) R (¬q)
            var notUntil = LtlFormula.Not(LtlFormula.Until(p, q));
            var releaseNeg = LtlFormula.Release(LtlFormula.Not(p), LtlFormula.Not(q));
            Assert.That(notUntil, Is.EqualTo(releaseNeg));

            // ¬(p R q) = (¬p) U (¬q)
            var notRelease = LtlFormula.Not(LtlFormula.Release(p, q));
            var untilNeg = LtlFormula.Until(LtlFormula.Not(p), LtlFormula.Not(q));
            Assert.That(notRelease, Is.EqualTo(untilNeg));
        }

        [Test]
        public void Negation_TrueFalseDual()
        {
            Assert.That(LtlFormula.Not(LtlFormula.True), Is.EqualTo(LtlFormula.False));
            Assert.That(LtlFormula.Not(LtlFormula.False), Is.EqualTo(LtlFormula.True));
        }

        [Test]
        public void NNF_ComplexFormula_NoInternalNegationOnCompoundNodes()
        {
            var p = P("p"); var q = P("q"); var r = P("r");
            // ¬((p U q) ∧ X r)
            //   → ¬(p U q) ∨ ¬X r
            //   → ((¬p) R (¬q)) ∨ X(¬r)
            var phi = LtlFormula.Not(LtlFormula.And(
                LtlFormula.Until(p, q),
                LtlFormula.Next(r)));
            var expected = LtlFormula.Or(
                LtlFormula.Release(LtlFormula.Not(p), LtlFormula.Not(q)),
                LtlFormula.Next(LtlFormula.Not(r)));
            Assert.That(phi, Is.EqualTo(expected));
        }

        [Test]
        public void ToString_IsStableAcrossEquivalentConstructions()
        {
            var p = P("p"); var q = P("q"); var r = P("r");
            var a = LtlFormula.And(p, LtlFormula.And(q, r));
            var b = LtlFormula.And(r, LtlFormula.And(q, p));
            var c = LtlFormula.And(q, LtlFormula.And(r, p));
            Assert.That(a.ToString(), Is.EqualTo(b.ToString()));
            Assert.That(a.ToString(), Is.EqualTo(c.ToString()));
        }
    }
}
