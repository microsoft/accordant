namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Pins the Σ*-prefix Union absorption rule:
    /// <c>R · Σ* · T  |  Σ* · T  ≡  Σ* · T</c>.
    ///
    /// <para>Soundness: any word in <c>L(R · Σ* · T)</c> decomposes as
    /// <c>r · w · t</c> with <c>r ∈ L(R)</c>, <c>w ∈ Σ*</c>, <c>t ∈ L(T)</c>;
    /// re-association puts <c>(r · w)</c> in <c>Σ*</c>, so the word also
    /// lies in <c>L(Σ* · T)</c>.</para>
    ///
    /// <para>Why it matters: this is the rewrite that collapses the
    /// regex-concat encoding of <c>∧ᵢ GFpᵢ</c> from <c>~2ⁿ</c> derivative
    /// classes down to the optimal <c>n+1</c>. The scaling probe
    /// <c>Report_DnfLeaves_vs_RltlRegex_Scaling</c> shows the effect on
    /// the model-checker-relevant negated form.</para>
    /// </summary>
    [TestFixture]
    public class EreUnionSigmaStarAbsorptionTests
    {
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static Ere<IStatePredicate> Atom(StateProp p)
            => Ere<IStatePredicate>.Atom(new StatePredAtom(p));

        private static Ere<IStatePredicate> SigmaStar()
            => Ere<IStatePredicate>.Star(Ere<IStatePredicate>.Sigma());

        [Test]
        public void SigmaStarS_AbsorbsRSigmaStarS()
        {
            var sStar = SigmaStar();
            var a = Atom(Pa);
            var b = Atom(Pb);

            // big = Σ* · b
            var big = Ere<IStatePredicate>.Concat(sStar, b);
            // small = a · Σ* · b  =  a · big (right-associated)
            var small = Ere<IStatePredicate>.Concat(a, big);

            var union = Ere<IStatePredicate>.Union(big, small);
            Assert.That(union, Is.SameAs(big),
                "Σ*·b should absorb a·Σ*·b in a union.");
        }

        [Test]
        public void SigmaStarST_AbsorbsRSigmaStarST()
        {
            var sStar = SigmaStar();
            var a = Atom(Pa);
            var b = Atom(Pb);

            // big = Σ* · a · b
            var big = Ere<IStatePredicate>.Concat(sStar,
                Ere<IStatePredicate>.Concat(a, b));
            // small = b · big = b · Σ* · a · b
            var small = Ere<IStatePredicate>.Concat(b, big);

            var union = Ere<IStatePredicate>.Union(big, small);
            Assert.That(union, Is.SameAs(big),
                "Σ*·a·b should absorb b·Σ*·a·b in a union.");
        }

        [Test]
        public void ChainedConcat_RegexFairnessProgressClasses()
        {
            // r1 = Σ*·a·Σ*·b   (one "progress step" remaining)
            // r2 = Σ*·b        (zero "progress steps" remaining; deepest)
            // The user's headline case: r1 + r2 should collapse to r2,
            // because L(r1) ⊆ L(r2).
            var sStar = SigmaStar();
            var a = Atom(Pa);
            var b = Atom(Pb);

            var r2 = Ere<IStatePredicate>.Concat(sStar, b);
            var r1 = Ere<IStatePredicate>.Concat(sStar,
                Ere<IStatePredicate>.Concat(a, r2));

            var union = Ere<IStatePredicate>.Union(r1, r2);
            Assert.That(union, Is.SameAs(r2),
                "Σ*·a·Σ*·b + Σ*·b should collapse to Σ*·b.");
        }

        [Test]
        public void NonSigmaPrefix_DoesNotAbsorb()
        {
            // a·b  + b  must NOT collapse: L(a·b) ⊄ L(b).
            // This guards against an over-eager generalisation that
            // would drop the Σ* requirement.
            var a = Atom(Pa);
            var b = Atom(Pb);

            var small = Ere<IStatePredicate>.Concat(a, b);
            var union = Ere<IStatePredicate>.Union(small, b);
            Assert.That(union, Is.Not.SameAs(small));
            Assert.That(union, Is.Not.SameAs(b),
                "a·b + b must remain a union; absorption requires Σ* in the 'big' operand.");
        }
    }
}
