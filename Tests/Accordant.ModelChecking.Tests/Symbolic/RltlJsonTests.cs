namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class RltlJsonTests
    {
        private static readonly RltlAlgebra<string> Alg =
            new RltlAlgebra<string>(StringFreeAlgebra.Instance);

        private static Rltl<string> A => Alg.Atom("a");
        private static Rltl<string> B => Alg.Atom("b");
        private static Ere<string> Ra => Ere<string>.Atom("a");
        private static Ere<string> Rb => Ere<string>.Atom("b");

        private static void RoundTrip(Rltl<string> phi)
        {
            var json = RltlJson.Serialize(phi);
            var back = RltlJson.Deserialize(json);
            Assert.That(back.Equals(phi), Is.True,
                $"Round-trip failed. JSON: {json}\nOriginal: {phi}\nGot: {back}");
        }

        [Test] public void True_()  => RoundTrip(Alg.True);
        [Test] public void False_() => RoundTrip(Alg.False);
        [Test] public void Atom_A() => RoundTrip(A);

        [Test]
        public void NegAtom_A()
        {
            // NegAtom("a") under StringFreeAlgebra becomes Atom("¬a").
            var na = Alg.NegAtom("a");
            RoundTrip(na);
        }

        [Test] public void Next_A()         => RoundTrip(Alg.Next(A));
        [Test] public void Until_AB()       => RoundTrip(Alg.Until(A, B));
        [Test] public void Release_AB()     => RoundTrip(Alg.Release(A, B));
        [Test] public void Eventually_A()   => RoundTrip(Alg.Eventually(A));
        [Test] public void Globally_A()     => RoundTrip(Alg.Globally(A));
        [Test] public void And_AB()         => RoundTrip(Alg.And(A, B));
        [Test] public void Or_AB()          => RoundTrip(Alg.Or(A, B));

        // RLTL-specific: embedded ERE.
        [Test] public void SeqPrefix_aB()      => RoundTrip(Alg.SeqPrefix(Ra, B));
        [Test] public void OvlPrefix_aB()      => RoundTrip(Alg.OvlPrefix(Ra, B));
        [Test] public void Trigger_aB()        => RoundTrip(Alg.Trigger(Ra, B));
        [Test] public void Match_aB()          => RoundTrip(Alg.Match(Ra, B));
        [Test] public void WeakClosure_a()     => RoundTrip(Alg.WeakClosure(Ra));
        [Test] public void NegWeakClosure_a()  => RoundTrip(Alg.NegWeakClosure(Ra));
        [Test] public void OmegaClosure_a()    => RoundTrip(Alg.OmegaClosure(Ra));

        [Test]
        public void Nested_AcrossEreAndRltl()
        {
            // G ( (a · b*) ; (a U b) )
            var inner = Alg.SeqPrefix(
                Ere<string>.Concat(Ra, Ere<string>.Star(Rb)),
                Alg.Until(A, B));
            RoundTrip(Alg.Globally(inner));
        }

        [Test]
        public void Implies_Sugar_DeserializeOnly()
        {
            var json = "{\"op\":\"Implies\","
                + "\"left\":{\"op\":\"Atom\",\"pred\":\"a\"},"
                + "\"right\":{\"op\":\"Atom\",\"pred\":\"b\"}}";
            var phi = RltlJson.Deserialize(json);
            var expected = Alg.Or(Alg.Not(A), B);
            Assert.That(phi.Equals(expected), Is.True);
        }
    }
}
