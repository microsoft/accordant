using Microsoft.Accordant;

namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Bdd;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    [TestFixture]
    public class RltlColourTests
    {
        private static readonly BddStatePropEba Eba = BddStatePropEba.Instance;
        private static readonly StateProp Pa = new StateProp("a", _ => true);
        private static readonly StateProp Pb = new StateProp("b", _ => true);

        private static RltlAlgebra<IStatePredicate> Alg => new RltlAlgebra<IStatePredicate>(Eba);

        // --- IsRejecting: matches ABW IsAccepting partition (Until = rejecting). ---

        [Test] public void Atom_IsSafety() =>
            Assert.That(RltlColour.IsRejecting(Alg.Atom(new StatePredAtom(Pa))), Is.False);

        [Test] public void Eventually_IsRejecting() =>
            Assert.That(RltlColour.IsRejecting(
                Alg.Eventually(Alg.Atom(new StatePredAtom(Pa)))), Is.True);

        [Test] public void Until_IsRejecting() =>
            Assert.That(RltlColour.IsRejecting(
                Alg.Until(Alg.Atom(new StatePredAtom(Pa)),
                          Alg.Atom(new StatePredAtom(Pb)))), Is.True);

        [Test] public void Globally_IsSafety() =>
            Assert.That(RltlColour.IsRejecting(
                Alg.Globally(Alg.Atom(new StatePredAtom(Pa)))), Is.False);

        [Test] public void Release_IsSafety() =>
            Assert.That(RltlColour.IsRejecting(
                Alg.Release(Alg.Atom(new StatePredAtom(Pa)),
                            Alg.Atom(new StatePredAtom(Pb)))), Is.False);

        [Test] public void Next_IsSafety() =>
            Assert.That(RltlColour.IsRejecting(
                Alg.Next(Alg.Atom(new StatePredAtom(Pa)))), Is.False);

        [Test] public void GFa_IsSafety_ButFa_IsRejecting()
        {
            var a = Alg.Atom(new StatePredAtom(Pa));
            var Fa = Alg.Eventually(a);
            var GFa = Alg.Globally(Fa);
            Assert.Multiple(() =>
            {
                Assert.That(RltlColour.IsRejecting(GFa), Is.False, "GFa head=R, ∉ F");
                Assert.That(RltlColour.IsRejecting(Fa),  Is.True,  "Fa head=U, ∈ F");
                Assert.That(RltlColour.SameColour(GFa, Fa), Is.False,
                    "Different colours: the macrostate-subsumption guard must block dropping Fa for GFa.");
            });
        }

        [Test] public void And_IsSafety_ByHead()
        {
            var a = Alg.Atom(new StatePredAtom(Pa));
            var b = Alg.Atom(new StatePredAtom(Pb));
            var and = Alg.And(Alg.Eventually(a), Alg.Globally(b)); // head = And
            Assert.That(RltlColour.IsRejecting(and), Is.False);
        }

        [Test] public void Or_IsSafety_ByHead()
        {
            var a = Alg.Atom(new StatePredAtom(Pa));
            var b = Alg.Atom(new StatePredAtom(Pb));
            var or = Alg.Or(Alg.Eventually(a), Alg.Globally(b)); // head = Or
            Assert.That(RltlColour.IsRejecting(or), Is.False);
        }

        [Test] public void SameColour_TwoUntils_True()
        {
            var Fa = Alg.Eventually(Alg.Atom(new StatePredAtom(Pa)));
            var Fb = Alg.Eventually(Alg.Atom(new StatePredAtom(Pb)));
            Assert.That(RltlColour.SameColour(Fa, Fb), Is.True);
        }

        [Test] public void SameColour_TwoReleases_True()
        {
            var Ga = Alg.Globally(Alg.Atom(new StatePredAtom(Pa)));
            var Gb = Alg.Globally(Alg.Atom(new StatePredAtom(Pb)));
            Assert.That(RltlColour.SameColour(Ga, Gb), Is.True);
        }

        // Sanity: the classifier matches RltlDerivative.IsAccepting head-by-head
        // for the non-WeakClosure heads we care about.
        [Test] public void MatchesDerivativeIsAccepting_OnCoreHeads()
        {
            var registry = new ConditionRegistry<IStatePredicate>(
                System.Collections.Generic.EqualityComparer<IStatePredicate>.Default);
            var deriv = new RltlDerivative<IStatePredicate, State>(Eba, registry, null, null);
            var a = Alg.Atom(new StatePredAtom(Pa));
            var b = Alg.Atom(new StatePredAtom(Pb));
            Rltl<IStatePredicate>[] cases =
            {
                a,
                Alg.Next(a),
                Alg.Eventually(a),
                Alg.Globally(a),
                Alg.Until(a, b),
                Alg.Release(a, b),
                Alg.And(a, b),
                Alg.Or(a, b),
            };
            foreach (var f in cases)
                Assert.That(RltlColour.IsRejecting(f), Is.EqualTo(!deriv.IsAccepting(f)),
                    $"Mismatch for {f}");
        }
    }
}
