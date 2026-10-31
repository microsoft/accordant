namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class EreJsonTests
    {
        private static Ere<string> A => Ere<string>.Atom("a");
        private static Ere<string> B => Ere<string>.Atom("b");

        private static void RoundTrip(Ere<string> r)
        {
            var json = EreJson.Serialize(r);
            var back = EreJson.Deserialize(json);
            Assert.That(back.Equals(r), Is.True,
                $"Round-trip failed. JSON: {json}\nOriginal: {r}\nGot: {back}");
        }

        [Test] public void Empty()        => RoundTrip(Ere<string>.Empty());
        [Test] public void Epsilon()      => RoundTrip(Ere<string>.Epsilon());
        [Test] public void Atom_Simple()  => RoundTrip(A);
        [Test] public void Concat_AB()    => RoundTrip(Ere<string>.Concat(A, B));
        [Test] public void Union_AB()     => RoundTrip(Ere<string>.Union(A, B));
        [Test] public void Intersect_AB() => RoundTrip(Ere<string>.Intersect(A, B));
        [Test] public void Star_A()       => RoundTrip(Ere<string>.Star(A));
        [Test] public void Complement_A() => RoundTrip(Ere<string>.Complement(A));
        [Test] public void Fusion_AB()    => RoundTrip(Ere<string>.Fusion(A, B));

        [Test]
        public void Xor_AB()
        {
            var r = Ere<string>.Xor(A, B);
            // r might be canonicalized — round-trip whatever it is.
            RoundTrip(r);
        }

        [Test]
        public void Complex_Nested()
        {
            // (a · b*) ∪ ~(a ∩ b)
            var r = Ere<string>.Union(
                Ere<string>.Concat(A, Ere<string>.Star(B)),
                Ere<string>.Complement(Ere<string>.Intersect(A, B)));
            RoundTrip(r);
        }

        [Test]
        public void Sugar_Plus_DeserializeOnly()
        {
            var r = EreJson.Deserialize("{\"op\":\"Plus\",\"inner\":{\"op\":\"Atom\",\"pred\":\"a\"}}");
            Assert.That(r.Equals(Ere<string>.Plus(A)), Is.True);
        }

        [Test]
        public void Sugar_Optional_DeserializeOnly()
        {
            var r = EreJson.Deserialize("{\"op\":\"Optional\",\"inner\":{\"op\":\"Atom\",\"pred\":\"a\"}}");
            Assert.That(r.Equals(Ere<string>.Optional(A)), Is.True);
        }

        [Test]
        public void Sugar_Sigma_DeserializeOnly()
        {
            var r = EreJson.Deserialize("{\"op\":\"Sigma\"}");
            Assert.That(r.Equals(Ere<string>.Sigma()), Is.True);
        }

        [Test]
        public void PredicateWithQuotesAndBackslashes()
        {
            var r = Ere<string>.Atom("p\"q\\r");
            RoundTrip(r);
        }
    }
}
