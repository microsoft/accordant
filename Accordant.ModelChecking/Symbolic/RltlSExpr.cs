namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Codec for embedding a <typeparamref name="TPred"/> predicate as an
    /// S-expression. Atoms in ERE / RLTL formulas carry a predicate value;
    /// the codec is the extension point that lets the DSL serialise and
    /// parse those predicates without knowing the concrete type.
    /// </summary>
    public interface IPredicateCodec<TPred>
    {
        /// <summary>Serialise a predicate to an S-expression.</summary>
        SExpr Print(TPred predicate);
        /// <summary>Parse an S-expression back to a predicate.</summary>
        TPred Parse(SExpr expr);
    }

    /// <summary>
    /// Default codec for <see cref="string"/>-valued predicates: prints as a
    /// bare atom (auto-quoted when it contains delimiters) and parses any
    /// <see cref="SAtom"/> by returning its raw <see cref="SAtom.Value"/>.
    /// Useful as the "eager fallback" requested by the DSL todo when no
    /// structured predicate codec is available.
    /// </summary>
    public sealed class StringPredicateCodec : IPredicateCodec<string>
    {
        public static readonly StringPredicateCodec Instance = new StringPredicateCodec();
        public SExpr Print(string predicate) => new SAtom(predicate ?? string.Empty);
        public string Parse(SExpr expr)
        {
            if (expr is SAtom a) return a.Value;
            throw new FormatException($"Expected a string predicate atom, got list: {expr}");
        }
    }

    /// <summary>
    /// S-expression surface DSL for ERE and RLTL formulas. The vocabulary is
    /// deliberately small and orthogonal:
    ///
    /// <para><b>ERE forms (constructors of <see cref="Ere{TPred}"/>):</b></para>
    /// <list type="table">
    ///   <item><term><c>empty</c></term><description>the empty language</description></item>
    ///   <item><term><c>eps</c></term><description>the empty word</description></item>
    ///   <item><term><c>(atom &lt;pred&gt;)</c></term><description>predicate atom</description></item>
    ///   <item><term><c>(concat r1 r2 ...)</c></term><description>n-ary, left-associated</description></item>
    ///   <item><term><c>(union r1 r2 ...)</c></term><description>n-ary union</description></item>
    ///   <item><term><c>(inter r1 r2 ...)</c></term><description>n-ary intersection</description></item>
    ///   <item><term><c>(star r)</c></term><description>Kleene star</description></item>
    ///   <item><term><c>(comp r)</c></term><description>complement</description></item>
    ///   <item><term><c>(fusion r1 r2)</c></term><description>fusion (binary)</description></item>
    ///   <item><term><c>(xor r1 r2)</c></term><description>symmetric difference (binary)</description></item>
    /// </list>
    ///
    /// <para><b>RLTL forms (constructors of <see cref="Rltl{TPred}"/>):</b></para>
    /// <list type="table">
    ///   <item><term><c>true</c> / <c>false</c></term><description>constants</description></item>
    ///   <item><term><c>(atom &lt;pred&gt;)</c></term><description>predicate atom</description></item>
    ///   <item><term><c>(X phi)</c></term><description>next; alias <c>(next phi)</c></description></item>
    ///   <item><term><c>(U l r)</c></term><description>until; alias <c>(until l r)</c></description></item>
    ///   <item><term><c>(R l r)</c></term><description>release; alias <c>(release l r)</c></description></item>
    ///   <item><term><c>(F phi)</c></term><description>eventually = <c>(U true phi)</c></description></item>
    ///   <item><term><c>(G phi)</c></term><description>globally = <c>(R false phi)</c></description></item>
    ///   <item><term><c>(and phi1 phi2 ...)</c></term><description>n-ary conjunction</description></item>
    ///   <item><term><c>(or phi1 phi2 ...)</c></term><description>n-ary disjunction</description></item>
    ///   <item><term><c>(seq R phi)</c></term><description>R ; phi</description></item>
    ///   <item><term><c>(ovl R phi)</c></term><description>R : phi</description></item>
    ///   <item><term><c>(trig R phi)</c></term><description>R ⊳ phi</description></item>
    ///   <item><term><c>(match R phi)</c></term><description>R ⊳⊳ phi</description></item>
    ///   <item><term><c>(wcl R)</c></term><description>weak closure {R}</description></item>
    ///   <item><term><c>(nwcl R)</c></term><description>negated weak closure</description></item>
    ///   <item><term><c>(ocl R)</c></term><description>ω-closure {R}ω</description></item>
    /// </list>
    ///
    /// <para>
    /// Predicates are embedded via an <see cref="IPredicateCodec{TPred}"/>
    /// supplied by the caller. The printer guarantees a round-trip: the
    /// output of <see cref="PrintEre{TPred}(Ere{TPred}, IPredicateCodec{TPred})"/>
    /// parsed back via <see cref="ParseEre{TPred}(string, IPredicateCodec{TPred})"/>
    /// yields a structurally equal expression (the same holds for RLTL).
    /// </para>
    /// </summary>
    public static class RltlSExpr
    {
        // ─────────────────────────────────────────────────────────────────
        // ERE → SExpr
        // ─────────────────────────────────────────────────────────────────

        public static SExpr ToSExpr<TPred>(Ere<TPred> ere, IPredicateCodec<TPred> codec)
        {
            if (ere == null) throw new ArgumentNullException(nameof(ere));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            return EreTo(ere, codec);
        }

        private static SExpr EreTo<TPred>(Ere<TPred> ere, IPredicateCodec<TPred> codec)
        {
            switch (ere)
            {
                case EreEmpty<TPred> _:    return new SAtom("empty");
                case EreEpsilon<TPred> _:  return new SAtom("eps");
                case EreAtom<TPred> a:     return new SList(new SAtom("atom"), codec.Print(a.Predicate));
                case EreConcat<TPred> c:   return Flatten("concat", c, codec);
                case EreUnion<TPred> u:    return Nary("union", u.Operands, codec);
                case EreIntersect<TPred> i:return Nary("inter", i.Operands, codec);
                case EreComplement<TPred> n: return new SList(new SAtom("comp"), EreTo(n.Inner, codec));
                case EreStar<TPred> s:     return new SList(new SAtom("star"), EreTo(s.Inner, codec));
                case EreFusion<TPred> f:   return new SList(new SAtom("fusion"), EreTo(f.Left, codec), EreTo(f.Right, codec));
                case EreXor<TPred> x:
                    var head = new SAtom(x.Negated ? "xnor" : "xor");
                    var items = new SExpr[x.Operands.Count + 1];
                    items[0] = head;
                    for (int i = 0; i < x.Operands.Count; i++) items[i + 1] = EreTo(x.Operands[i], codec);
                    return new SList(items);
                default: throw new ArgumentException($"Unknown ERE node type: {ere.GetType().Name}");
            }
        }

        private static SExpr Nary<TPred>(string head, IReadOnlyList<Ere<TPred>> ops, IPredicateCodec<TPred> codec)
        {
            var items = new SExpr[ops.Count + 1];
            items[0] = new SAtom(head);
            for (int i = 0; i < ops.Count; i++) items[i + 1] = EreTo(ops[i], codec);
            return new SList(items);
        }

        // EreConcat is binary in the AST; collect a left-associated chain
        // into a flat (concat ...) form for cleaner output.
        private static SExpr Flatten<TPred>(string head, EreConcat<TPred> top, IPredicateCodec<TPred> codec)
        {
            var collected = new List<Ere<TPred>>();
            void Walk(Ere<TPred> e)
            {
                if (e is EreConcat<TPred> c) { Walk(c.Left); Walk(c.Right); }
                else collected.Add(e);
            }
            Walk(top);
            var items = new SExpr[collected.Count + 1];
            items[0] = new SAtom(head);
            for (int i = 0; i < collected.Count; i++) items[i + 1] = EreTo(collected[i], codec);
            return new SList(items);
        }

        // ─────────────────────────────────────────────────────────────────
        // SExpr → ERE
        // ─────────────────────────────────────────────────────────────────

        public static Ere<TPred> EreFromSExpr<TPred>(SExpr expr, IPredicateCodec<TPred> codec)
        {
            if (expr == null) throw new ArgumentNullException(nameof(expr));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            return ParseEreS(expr, codec);
        }

        private static Ere<TPred> ParseEreS<TPred>(SExpr expr, IPredicateCodec<TPred> codec)
        {
            if (expr is SAtom a)
            {
                switch (a.Value)
                {
                    case "empty": return EreEmpty<TPred>.Instance;
                    case "eps":   return EreEpsilon<TPred>.Instance;
                    default:
                        throw new FormatException(
                            $"Unknown ERE atom '{a.Value}'. Expected 'empty', 'eps', or a parenthesised form.");
                }
            }
            var list = (SList)expr;
            if (list.Items.Count == 0)
                throw new FormatException("Empty list is not a valid ERE form.");
            var head = ExpectAtom(list.Items[0], "ERE head");
            switch (head)
            {
                case "atom":
                    Expect(list, 2, "atom");
                    return Ere<TPred>.Atom(codec.Parse(list.Items[1]));
                case "concat":
                    return FoldBinaryEre(list, codec, Ere<TPred>.Concat, minArity: 1);
                case "union":
                    return FoldBinaryEre(list, codec, Ere<TPred>.Union, minArity: 1);
                case "inter":
                    return FoldBinaryEre(list, codec, Ere<TPred>.Intersect, minArity: 1);
                case "comp":
                    Expect(list, 2, "comp");
                    return Ere<TPred>.Complement(ParseEreS<TPred>(list.Items[1], codec));
                case "star":
                    Expect(list, 2, "star");
                    return Ere<TPred>.Star(ParseEreS<TPred>(list.Items[1], codec));
                case "fusion":
                    Expect(list, 3, "fusion");
                    return Ere<TPred>.Fusion(
                        ParseEreS<TPred>(list.Items[1], codec),
                        ParseEreS<TPred>(list.Items[2], codec));
                case "xor":
                    return FoldBinaryEre(list, codec, Ere<TPred>.Xor, minArity: 2);
                case "xnor":
                    return FoldBinaryEre(list, codec, Ere<TPred>.Xnor, minArity: 2);
                default:
                    throw new FormatException($"Unknown ERE head '{head}'.");
            }
        }

        private static Ere<TPred> FoldBinaryEre<TPred>(
            SList list, IPredicateCodec<TPred> codec,
            Func<Ere<TPred>, Ere<TPred>, Ere<TPred>> combine,
            int minArity)
        {
            int arity = list.Items.Count - 1;
            if (arity < minArity)
                throw new FormatException(
                    $"'{((SAtom)list.Items[0]).Value}' expects at least {minArity} operand(s), got {arity}.");
            var acc = ParseEreS<TPred>(list.Items[1], codec);
            for (int i = 2; i < list.Items.Count; i++)
                acc = combine(acc, ParseEreS<TPred>(list.Items[i], codec));
            return acc;
        }

        // ─────────────────────────────────────────────────────────────────
        // RLTL → SExpr
        // ─────────────────────────────────────────────────────────────────

        public static SExpr ToSExpr<TPred>(Rltl<TPred> phi, IPredicateCodec<TPred> codec)
        {
            if (phi == null) throw new ArgumentNullException(nameof(phi));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            return RltlTo(phi, codec);
        }

        private static SExpr RltlTo<TPred>(Rltl<TPred> phi, IPredicateCodec<TPred> codec)
        {
            switch (phi)
            {
                case RltlTrue<TPred> _:  return new SAtom("true");
                case RltlFalse<TPred> _: return new SAtom("false");
                case RltlAtom<TPred> a:  return new SList(new SAtom("atom"), codec.Print(a.Predicate));
                case RltlNext<TPred> n:  return new SList(new SAtom("X"), RltlTo(n.Inner, codec));
                case RltlUntil<TPred> u when u.Left is RltlTrue<TPred>:
                    return new SList(new SAtom("F"), RltlTo(u.Right, codec));
                case RltlRelease<TPred> r when r.Left is RltlFalse<TPred>:
                    return new SList(new SAtom("G"), RltlTo(r.Right, codec));
                case RltlUntil<TPred> u:
                    return new SList(new SAtom("U"), RltlTo(u.Left, codec), RltlTo(u.Right, codec));
                case RltlRelease<TPred> r:
                    return new SList(new SAtom("R"), RltlTo(r.Left, codec), RltlTo(r.Right, codec));
                case RltlAnd<TPred> a:   return RltlNary("and", a.Operands, codec);
                case RltlOr<TPred> o:    return RltlNary("or",  o.Operands, codec);
                case RltlSeqPrefix<TPred> s:
                    return new SList(new SAtom("seq"), EreTo(s.Regex, codec), RltlTo(s.Phi, codec));
                case RltlOvlPrefix<TPred> v:
                    return new SList(new SAtom("ovl"), EreTo(v.Regex, codec), RltlTo(v.Phi, codec));
                case RltlTrigger<TPred> t:
                    return new SList(new SAtom("trig"), EreTo(t.Regex, codec), RltlTo(t.Phi, codec));
                case RltlMatch<TPred> m:
                    return new SList(new SAtom("match"), EreTo(m.Regex, codec), RltlTo(m.Phi, codec));
                case RltlWeakClosure<TPred> w:
                    return new SList(new SAtom("wcl"),  EreTo(w.Regex, codec));
                case RltlNegWeakClosure<TPred> nw:
                    return new SList(new SAtom("nwcl"), EreTo(nw.Regex, codec));
                case RltlOmegaClosure<TPred> oc:
                    return new SList(new SAtom("ocl"),  EreTo(oc.Regex, codec));
                default:
                    throw new ArgumentException($"Unknown RLTL node type: {phi.GetType().Name}");
            }
        }

        private static SExpr RltlNary<TPred>(string head, IReadOnlyList<Rltl<TPred>> ops, IPredicateCodec<TPred> codec)
        {
            var items = new SExpr[ops.Count + 1];
            items[0] = new SAtom(head);
            for (int i = 0; i < ops.Count; i++) items[i + 1] = RltlTo(ops[i], codec);
            return new SList(items);
        }

        // ─────────────────────────────────────────────────────────────────
        // SExpr → RLTL
        // ─────────────────────────────────────────────────────────────────

        public static Rltl<TPred> RltlFromSExpr<TPred>(SExpr expr, IPredicateCodec<TPred> codec)
        {
            if (expr == null) throw new ArgumentNullException(nameof(expr));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            return ParseRltlS(expr, codec);
        }

        private static Rltl<TPred> ParseRltlS<TPred>(SExpr expr, IPredicateCodec<TPred> codec)
        {
            if (expr is SAtom a)
            {
                switch (a.Value)
                {
                    case "true":  return RltlTrue<TPred>.Instance;
                    case "false": return RltlFalse<TPred>.Instance;
                    default:
                        throw new FormatException(
                            $"Unknown RLTL atom '{a.Value}'. Expected 'true', 'false', or a parenthesised form.");
                }
            }
            var list = (SList)expr;
            if (list.Items.Count == 0)
                throw new FormatException("Empty list is not a valid RLTL form.");
            var head = ExpectAtom(list.Items[0], "RLTL head");
            switch (head)
            {
                case "atom":
                    Expect(list, 2, "atom");
                    return Rltl<TPred>.Atom(codec.Parse(list.Items[1]));
                case "X":
                case "next":
                    Expect(list, 2, head);
                    return Rltl<TPred>.Next(ParseRltlS<TPred>(list.Items[1], codec));
                case "U":
                case "until":
                    Expect(list, 3, head);
                    return Rltl<TPred>.Until(
                        ParseRltlS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "R":
                case "release":
                    Expect(list, 3, head);
                    return Rltl<TPred>.Release(
                        ParseRltlS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "F":
                    Expect(list, 2, head);
                    return Rltl<TPred>.Eventually(ParseRltlS<TPred>(list.Items[1], codec));
                case "G":
                    Expect(list, 2, head);
                    return Rltl<TPred>.Globally(ParseRltlS<TPred>(list.Items[1], codec));
                case "and":
                    return BuildRltlAnd(ParseRltlOperands(list, codec));
                case "or":
                    return BuildRltlOr(ParseRltlOperands(list, codec));
                case "seq":
                    Expect(list, 3, head);
                    return Rltl<TPred>.SeqPrefix(
                        ParseEreS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "ovl":
                    Expect(list, 3, head);
                    return Rltl<TPred>.OvlPrefix(
                        ParseEreS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "trig":
                    Expect(list, 3, head);
                    return Rltl<TPred>.Trigger(
                        ParseEreS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "match":
                    Expect(list, 3, head);
                    return Rltl<TPred>.Match(
                        ParseEreS<TPred>(list.Items[1], codec),
                        ParseRltlS<TPred>(list.Items[2], codec));
                case "wcl":
                    Expect(list, 2, head);
                    return Rltl<TPred>.WeakClosure(ParseEreS<TPred>(list.Items[1], codec));
                case "nwcl":
                    Expect(list, 2, head);
                    return Rltl<TPred>.NegWeakClosure(ParseEreS<TPred>(list.Items[1], codec));
                case "ocl":
                    Expect(list, 2, head);
                    return Rltl<TPred>.OmegaClosure(ParseEreS<TPred>(list.Items[1], codec));
                default:
                    throw new FormatException($"Unknown RLTL head '{head}'.");
            }
        }

        private static Rltl<TPred>[] ParseRltlOperands<TPred>(SList list, IPredicateCodec<TPred> codec)
        {
            var ops = new Rltl<TPred>[list.Items.Count - 1];
            for (int i = 1; i < list.Items.Count; i++)
                ops[i - 1] = ParseRltlS<TPred>(list.Items[i], codec);
            return ops;
        }

        // ACI-normalized RLTL And construction without atom fusion. The DSL
        // is a faithful syntactic surface; semantic atom fusion is the job of
        // <see cref="RltlAlgebra{TPred}"/> when the caller has an EBA in hand.
        private static Rltl<TPred> BuildRltlAnd<TPred>(IReadOnlyList<Rltl<TPred>> ops)
        {
            var set = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            foreach (var op in ops)
            {
                if (op is RltlFalse<TPred>) return RltlFalse<TPred>.Instance;
                if (op is RltlTrue<TPred>) continue;
                if (op is RltlAnd<TPred> a) foreach (var sub in a.Operands) set.Add(sub);
                else set.Add(op);
            }
            if (set.Count == 0) return RltlTrue<TPred>.Instance;
            if (set.Count == 1)
            {
                foreach (var only in set) return only;
            }
            var arr = new Rltl<TPred>[set.Count];
            set.CopyTo(arr);
            return Rltl<TPred>.DefaultBuilder.Intern(new RltlAnd<TPred>(arr));
        }

        private static Rltl<TPred> BuildRltlOr<TPred>(IReadOnlyList<Rltl<TPred>> ops)
        {
            var set = new SortedSet<Rltl<TPred>>(RltlComparer<TPred>.Instance);
            foreach (var op in ops)
            {
                if (op is RltlTrue<TPred>) return RltlTrue<TPred>.Instance;
                if (op is RltlFalse<TPred>) continue;
                if (op is RltlOr<TPred> o) foreach (var sub in o.Operands) set.Add(sub);
                else set.Add(op);
            }
            if (set.Count == 0) return RltlFalse<TPred>.Instance;
            if (set.Count == 1)
            {
                foreach (var only in set) return only;
            }
            var arr = new Rltl<TPred>[set.Count];
            set.CopyTo(arr);
            return Rltl<TPred>.DefaultBuilder.Intern(new RltlOr<TPred>(arr));
        }

        // ─────────────────────────────────────────────────────────────────
        // String convenience facade
        // ─────────────────────────────────────────────────────────────────

        public static string PrintEre<TPred>(Ere<TPred> ere, IPredicateCodec<TPred> codec)
            => SExprPrinter.Print(ToSExpr(ere, codec));

        public static string PrintRltl<TPred>(Rltl<TPred> phi, IPredicateCodec<TPred> codec)
            => SExprPrinter.Print(ToSExpr(phi, codec));

        public static Ere<TPred> ParseEre<TPred>(string text, IPredicateCodec<TPred> codec)
            => EreFromSExpr(SExprParser.Parse(text), codec);

        public static Rltl<TPred> ParseRltl<TPred>(string text, IPredicateCodec<TPred> codec)
            => RltlFromSExpr(SExprParser.Parse(text), codec);

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static void Expect(SList list, int expectedCount, string head)
        {
            if (list.Items.Count != expectedCount)
                throw new FormatException(
                    $"'{head}' expects {expectedCount - 1} operand(s), got {list.Items.Count - 1}.");
        }

        private static string ExpectAtom(SExpr expr, string ctx)
        {
            if (expr is SAtom a && !a.Quoted) return a.Value;
            throw new FormatException($"Expected {ctx} (bare atom), got: {expr}");
        }
    }
}
