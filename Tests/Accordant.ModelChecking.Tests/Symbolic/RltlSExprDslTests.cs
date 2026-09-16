namespace Accordant.ModelChecking.Tests.Symbolic
{
    using System;
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Round-trip and shape tests for the S-expression surface DSL
    /// (<see cref="RltlSExpr"/>). Predicates are opaque strings via
    /// <see cref="StringPredicateCodec"/>; structural equality on the
    /// resulting AST is the round-trip oracle (terms are hash-consed, so
    /// reference equality is equivalent to structural equality).
    /// </summary>
    [TestFixture]
    public class RltlSExprDslTests
    {
        private static readonly IPredicateCodec<string> Codec = StringPredicateCodec.Instance;

        // ─── S-expression lexer / printer ────────────────────────────────

        [Test]
        public void SExpr_Parse_Atom()
        {
            var s = SExpr.Parse("hello");
            Assert.That(s, Is.InstanceOf<SAtom>());
            Assert.That(((SAtom)s).Value, Is.EqualTo("hello"));
        }

        [Test]
        public void SExpr_Parse_QuotedString()
        {
            var s = SExpr.Parse("\"hello world\\n\"");
            Assert.That(((SAtom)s).Value, Is.EqualTo("hello world\n"));
            Assert.That(((SAtom)s).Quoted, Is.True);
        }

        [Test]
        public void SExpr_Parse_List()
        {
            var s = (SList)SExpr.Parse("(a (b c) d)");
            Assert.That(s.Items.Count, Is.EqualTo(3));
            Assert.That(((SAtom)s.Items[0]).Value, Is.EqualTo("a"));
            Assert.That(s.Items[1], Is.InstanceOf<SList>());
            Assert.That(((SAtom)s.Items[2]).Value, Is.EqualTo("d"));
        }

        [Test]
        public void SExpr_Parse_IgnoresComments()
        {
            var s = SExpr.Parse(@"
                ; leading comment
                (and ; trailing
                     a b)");
            Assert.That(s.ToString(), Is.EqualTo("(and a b)"));
        }

        [Test]
        public void SExpr_Print_AutoQuotesWhenNeeded()
        {
            var s = new SAtom("hello world");
            Assert.That(s.ToString(), Is.EqualTo("\"hello world\""));
        }

        [Test]
        public void SExpr_Print_RoundTripEscapes()
        {
            var orig = "tab\there\nnewline\\backslash\"quote";
            var s = new SAtom(orig);
            var parsed = (SAtom)SExpr.Parse(s.ToString());
            Assert.That(parsed.Value, Is.EqualTo(orig));
        }

        [Test]
        public void SExpr_Parse_RejectsUnclosedList()
        {
            Assert.Throws<FormatException>(() => SExpr.Parse("(a b"));
        }

        // ─── ERE round-trip ──────────────────────────────────────────────

        private static void EreRoundTrip(Ere<string> ere, string expectedSexpr)
        {
            var printed = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(printed, Is.EqualTo(expectedSexpr), "Print form");
            var parsed = RltlSExpr.ParseEre(printed, Codec);
            Assert.That(parsed, Is.SameAs(ere),
                $"Round-trip failed: parsed = {RltlSExpr.PrintEre(parsed, Codec)}");
        }

        [Test]
        public void Ere_Empty_RoundTrips()
            => EreRoundTrip(Ere<string>.Empty(), "empty");

        [Test]
        public void Ere_Epsilon_RoundTrips()
            => EreRoundTrip(Ere<string>.Epsilon(), "eps");

        [Test]
        public void Ere_Atom_RoundTrips()
            => EreRoundTrip(Ere<string>.Atom("p"), "(atom p)");

        [Test]
        public void Ere_Atom_QuotedPredicate_RoundTrips()
        {
            // Predicate name has spaces  must be emitted as a quoted string.
            var ere = Ere<string>.Atom("foo bar");
            var printed = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(printed, Is.EqualTo("(atom \"foo bar\")"));
            Assert.That(RltlSExpr.ParseEre(printed, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_Star_RoundTrips()
            => EreRoundTrip(Ere<string>.Star(Ere<string>.Atom("a")), "(star (atom a))");

        [Test]
        public void Ere_Complement_RoundTrips()
            => EreRoundTrip(Ere<string>.Complement(Ere<string>.Atom("a")), "(comp (atom a))");

        [Test]
        public void Ere_Concat_RoundTrips_LeftAssociated()
        {
            var ere = Ere<string>.Concat(
                Ere<string>.Concat(Ere<string>.Atom("a"), Ere<string>.Atom("b")),
                Ere<string>.Atom("c"));
            EreRoundTrip(ere, "(concat (atom a) (atom b) (atom c))");
        }

        [Test]
        public void Ere_Union_RoundTrips()
        {
            var ere = Ere<string>.Union(Ere<string>.Atom("a"), Ere<string>.Atom("b"));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_Intersect_RoundTrips()
        {
            var ere = Ere<string>.Intersect(
                Ere<string>.Star(Ere<string>.Atom("a")),
                Ere<string>.Star(Ere<string>.Atom("b")));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_Fusion_RoundTrips()
        {
            var ere = Ere<string>.Fusion(Ere<string>.Atom("a"), Ere<string>.Atom("b"));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(s, Is.EqualTo("(fusion (atom a) (atom b))"));
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_Xor_RoundTrips()
        {
            var ere = Ere<string>.Xor(Ere<string>.Atom("a"), Ere<string>.Atom("b"));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_Xnor_RoundTrips()
        {
            var ere = Ere<string>.Xnor(Ere<string>.Atom("a"), Ere<string>.Atom("b"));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        [Test]
        public void Ere_NestedComplex_RoundTrips()
        {
            // ~( (a · b)* + ε )
            var ere = Ere<string>.Complement(
                Ere<string>.Union(
                    Ere<string>.Star(Ere<string>.Concat(Ere<string>.Atom("a"), Ere<string>.Atom("b"))),
                    Ere<string>.Epsilon()));
            var s = RltlSExpr.PrintEre(ere, Codec);
            Assert.That(RltlSExpr.ParseEre(s, Codec), Is.SameAs(ere));
        }

        // ─── RLTL round-trip ─────────────────────────────────────────────

        private static void RltlRoundTrip(Rltl<string> phi, string expectedSexpr)
        {
            var printed = RltlSExpr.PrintRltl(phi, Codec);
            Assert.That(printed, Is.EqualTo(expectedSexpr), "Print form");
            var parsed = RltlSExpr.ParseRltl(printed, Codec);
            Assert.That(parsed, Is.SameAs(phi),
                $"Round-trip failed: parsed = {RltlSExpr.PrintRltl(parsed, Codec)}");
        }

        [Test]
        public void Rltl_True_RoundTrips() => RltlRoundTrip(Rltl<string>.True(), "true");

        [Test]
        public void Rltl_False_RoundTrips() => RltlRoundTrip(Rltl<string>.False(), "false");

        [Test]
        public void Rltl_Atom_RoundTrips() => RltlRoundTrip(Rltl<string>.Atom("p"), "(atom p)");

        [Test]
        public void Rltl_Next_RoundTrips()
            => RltlRoundTrip(Rltl<string>.Next(Rltl<string>.Atom("p")), "(X (atom p))");

        [Test]
        public void Rltl_Until_RoundTrips()
            => RltlRoundTrip(
                Rltl<string>.Until(Rltl<string>.Atom("p"), Rltl<string>.Atom("q")),
                "(U (atom p) (atom q))");

        [Test]
        public void Rltl_Release_RoundTrips()
            => RltlRoundTrip(
                Rltl<string>.Release(Rltl<string>.Atom("p"), Rltl<string>.Atom("q")),
                "(R (atom p) (atom q))");

        [Test]
        public void Rltl_Eventually_PrintsAsF()
            => RltlRoundTrip(Rltl<string>.Eventually(Rltl<string>.Atom("p")), "(F (atom p))");

        [Test]
        public void Rltl_Globally_PrintsAsG()
            => RltlRoundTrip(Rltl<string>.Globally(Rltl<string>.Atom("p")), "(G (atom p))");

        [Test]
        public void Rltl_Parse_AcceptsUntilAlias()
        {
            var expected = Rltl<string>.Until(Rltl<string>.Atom("p"), Rltl<string>.Atom("q"));
            Assert.That(RltlSExpr.ParseRltl("(until (atom p) (atom q))", Codec), Is.SameAs(expected));
        }

        [Test]
        public void Rltl_Parse_AcceptsNextAlias()
        {
            var expected = Rltl<string>.Next(Rltl<string>.Atom("p"));
            Assert.That(RltlSExpr.ParseRltl("(next (atom p))", Codec), Is.SameAs(expected));
        }

        [Test]
        public void Rltl_And_RoundTrips()
        {
            // Build via the printer round-trip (And constructor is internal,
            // so we go through the parser as the canonical entry point).
            var phi = RltlSExpr.ParseRltl("(and (atom a) (atom b) (atom c))", Codec);
            Assert.That(phi, Is.InstanceOf<RltlAnd<string>>());
            var s = RltlSExpr.PrintRltl(phi, Codec);
            Assert.That(RltlSExpr.ParseRltl(s, Codec), Is.SameAs(phi));
        }

        [Test]
        public void Rltl_Or_RoundTrips()
        {
            var phi = RltlSExpr.ParseRltl("(or (atom a) (atom b))", Codec);
            Assert.That(phi, Is.InstanceOf<RltlOr<string>>());
            var s = RltlSExpr.PrintRltl(phi, Codec);
            Assert.That(RltlSExpr.ParseRltl(s, Codec), Is.SameAs(phi));
        }

        [Test]
        public void Rltl_And_FlattensAndDedups()
        {
            // (and (and p q) p)  (and p q)
            var phi = RltlSExpr.ParseRltl("(and (and (atom p) (atom q)) (atom p))", Codec);
            var expected = RltlSExpr.ParseRltl("(and (atom p) (atom q))", Codec);
            Assert.That(phi, Is.SameAs(expected));
        }

        [Test]
        public void Rltl_SeqPrefix_RoundTrips()
        {
            var phi = Rltl<string>.SeqPrefix(
                Ere<string>.Star(Ere<string>.Atom("a")),
                Rltl<string>.Atom("p"));
            var s = RltlSExpr.PrintRltl(phi, Codec);
            Assert.That(s, Is.EqualTo("(seq (star (atom a)) (atom p))"));
            Assert.That(RltlSExpr.ParseRltl(s, Codec), Is.SameAs(phi));
        }

        [Test]
        public void Rltl_OvlPrefix_RoundTrips()
        {
            var phi = Rltl<string>.OvlPrefix(
                Ere<string>.Atom("a"), Rltl<string>.Atom("p"));
            RltlRoundTrip(phi, "(ovl (atom a) (atom p))");
        }

        [Test]
        public void Rltl_Trigger_RoundTrips()
        {
            var phi = Rltl<string>.Trigger(
                Ere<string>.Atom("a"), Rltl<string>.Atom("p"));
            RltlRoundTrip(phi, "(trig (atom a) (atom p))");
        }

        [Test]
        public void Rltl_Match_RoundTrips()
        {
            var phi = Rltl<string>.Match(
                Ere<string>.Atom("a"), Rltl<string>.Atom("p"));
            RltlRoundTrip(phi, "(match (atom a) (atom p))");
        }

        [Test]
        public void Rltl_WeakClosure_RoundTrips()
        {
            var phi = Rltl<string>.WeakClosure(Ere<string>.Atom("a"));
            RltlRoundTrip(phi, "(wcl (atom a))");
        }

        [Test]
        public void Rltl_NegWeakClosure_RoundTrips()
        {
            var phi = Rltl<string>.NegWeakClosure(Ere<string>.Atom("a"));
            RltlRoundTrip(phi, "(nwcl (atom a))");
        }

        [Test]
        public void Rltl_OmegaClosure_RoundTrips()
        {
            var phi = Rltl<string>.OmegaClosure(Ere<string>.Atom("a"));
            RltlRoundTrip(phi, "(ocl (atom a))");
        }

        [Test]
        public void Rltl_DeeplyNested_RoundTrips()
        {
            // □(p  ◇q)   (G (or (atom p) (F (atom q))))
            var phi = Rltl<string>.Globally(
                RltlSExpr.ParseRltl("(or (atom p) (F (atom q)))", Codec));
            var s = RltlSExpr.PrintRltl(phi, Codec);
            Assert.That(RltlSExpr.ParseRltl(s, Codec), Is.SameAs(phi));
        }

        // ─── Parse error reporting ───────────────────────────────────────

        [Test]
        public void Parse_UnknownEreHead_Throws()
        {
            var ex = Assert.Throws<FormatException>(
                () => RltlSExpr.ParseEre("(banana (atom a))", Codec));
            StringAssert.Contains("banana", ex.Message);
        }

        [Test]
        public void Parse_UnknownRltlHead_Throws()
        {
            var ex = Assert.Throws<FormatException>(
                () => RltlSExpr.ParseRltl("(banana (atom a))", Codec));
            StringAssert.Contains("banana", ex.Message);
        }

        [Test]
        public void Parse_WrongArity_Throws()
        {
            Assert.Throws<FormatException>(
                () => RltlSExpr.ParseRltl("(X (atom p) (atom q))", Codec));
        }

        [Test]
        public void Parse_UnknownAtomSymbol_Throws()
        {
            Assert.Throws<FormatException>(() => RltlSExpr.ParseRltl("undefined", Codec));
        }
    }
}
