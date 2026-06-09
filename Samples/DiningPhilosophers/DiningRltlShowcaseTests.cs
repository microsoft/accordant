namespace DiningPhilosophers
{
    using Microsoft.Accordant;
    using Microsoft.Accordant.ModelChecking;
    using Microsoft.Accordant.ModelChecking.Rltl;
    using NUnit.Framework;

    /// <summary>
    /// RLTL showcase: properties expressed using regular-expression
    /// combinators (<see cref="Regex"/>, <see cref="RltlFormula.Match"/>,
    /// <see cref="RltlFormula.Trigger"/>, …). These complement the
    /// pure-temporal tests in <see cref="DiningRltlTests"/> by exercising
    /// the regex layer of the DSL on the dining-philosophers model.
    /// </summary>
    public class DiningRltlShowcaseTests
    {
        private StateGraphNode _asymRoot;

        private static readonly Fairness PhilFairness =
            Fairness.StrongFair(sf => sf is Dining.PhilStep);

        [SetUp]
        public void Setup() =>
            _asymRoot = StateGraph.ExploreStateGraph(Dining.AllSteps(asymmetric: true), Dining.InitialState());

        private static Regex REat0 => Regex.Prop(Dining.Eating0, "Eating0");
        private static Regex REat1 => Regex.Prop(Dining.Eating1, "Eating1");
        private static Regex REat2 => Regex.Prop(Dining.Eating2, "Eating2");
        private static Regex RTwoEating => Regex.Prop(Dining.TwoEating, "TwoEating");
        private static Regex SigmaStar => Regex.Star(Regex.Sigma);

        private static RltlFormula NotTwoEating =>
            RltlFormula.Not(RltlFormula.Prop(Dining.TwoEating, "TwoEating"));

        /// <summary>
        /// Mutual-exclusion expressed as a regex trigger:
        /// the prefix <c>Σ* · TwoEating</c> can never match — i.e., no
        /// reachable run hits a state where two philosophers are eating.
        /// The same property as <see cref="DiningRltlTests.Safety_NoTwoEating_Asymmetric"/>
        /// but phrased in regex-prefix form so the trigger fires the
        /// moment the bad witness is observed.
        /// </summary>
        [Test]
        public void Safety_TwoEating_NeverMatches_AsRegexTrigger()
        {
            var bad = Regex.Concat(SigmaStar, RTwoEating);
            var phi = RltlFormula.Trigger(bad, RltlFormula.False);

            var result = RltlCheck.Check(_asymRoot, phi);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Match-style safety: at every position reached after seeing
        /// <see cref="Dining.Eating0"/>, the system must not be in a
        /// "two philosophers eating" state. Uses the overlapping match
        /// combinator <c>R ⊳⊳ φ</c>, which aligns the suffix obligation
        /// with the last consumed letter rather than the one after it.
        /// </summary>
        [Test]
        public void Safety_AfterEating0_NoTwoEating_AsOverlappingMatch()
        {
            var prefix = Regex.Concat(SigmaStar, REat0);
            var phi = RltlFormula.Match(prefix, NotTwoEating);

            var result = RltlCheck.Check(_asymRoot, phi);
            Assert.That(result.Valid, Is.True, result.GetTraceString());
        }

        /// <summary>
        /// Round-robin sighting (existential check via negation): show
        /// that the asymmetric variant <em>can</em> exhibit a phil0 →
        /// phil1 → phil2 eating sequence by negating "no such prefix
        /// ever exists" and asserting the negation fails.
        ///
        /// The regex requires three Eating events in order, separated by
        /// arbitrary intermediate states. <see cref="RltlFormula.SeqPrefix"/>
        /// is existential per run: <c>R ; φ</c> = ∃k. w[0..k] ∈ L(R) ∧
        /// w[k..] ⊨ φ. RltlCheck reports a counterexample when it finds
        /// a run that violates a property; here we phrase the dual.
        /// </summary>
        [Test]
        public void Witness_PhilZeroOneTwoEatInOrder_Exists()
        {
            var round = Regex.Concat(SigmaStar, REat0);
            round = Regex.Concat(round, SigmaStar);
            round = Regex.Concat(round, REat1);
            round = Regex.Concat(round, SigmaStar);
            round = Regex.Concat(round, REat2);

            // "The 0-1-2 eating sequence never appears" — should FAIL under
            // strong PhilFairness because every philosopher eats infinitely
            // often in the asymmetric variant.
            var noSuchOrder = RltlFormula.Trigger(round, RltlFormula.False);

            var result = RltlCheck.Check(_asymRoot, noSuchOrder, fairness: PhilFairness);
            Assert.That(result.Valid, Is.False,
                "Asymmetric DP admits runs in which phil0, phil1, phil2 eat in that order.");
        }

        /// <summary>
        /// Bounded-burst constraint: between any two consecutive
        /// <see cref="Dining.Eating0"/> events, some other philosopher
        /// must eat in between. Phrased as a forbidden regex prefix:
        /// <c>Σ* · Eating0 · (¬Eating0)* · Eating0</c> — if this prefix
        /// matches, none of the gap states had Eating0 (by construction)
        /// but we additionally require that the gap also lacked
        /// Eating1 and Eating2 — i.e., a "burst" of phil0-only eating.
        /// Should fail in the asymmetric model under <see cref="PhilFairness"/>:
        /// phil0 can transition Hungry → Eating → Hungry → Eating without
        /// phil1 or phil2 necessarily eating in between. Surfaced via
        /// counterexample.
        /// </summary>
        [Test]
        public void Counterexample_TwoEating0WithoutOthersInBetween_Exists()
        {
            var notOtherEat = Regex.Prop(
                (IState s) => !Dining.Eating1(s) && !Dining.Eating2(s),
                "¬Eating1 ∧ ¬Eating2");
            var burst = Regex.Concat(SigmaStar, REat0);
            burst = Regex.Concat(burst, Regex.Star(notOtherEat));
            burst = Regex.Concat(burst, REat0);

            var noBurst = RltlFormula.Trigger(burst, RltlFormula.False);

            var result = RltlCheck.Check(_asymRoot, noBurst, fairness: PhilFairness);
            Assert.That(result.Valid, Is.False,
                "Asymmetric DP allows phil0 to eat twice with no Eating1/Eating2 in between.");
        }
    }
}
