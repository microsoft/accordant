namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// EREQ extension of <see cref="RltlSExpr"/>: the same S-expression DSL
    /// extended with the two EREQ Phase-2 forms
    ///
    /// <list type="bullet">
    ///   <item><term><c>(prop NAME)</c></term>
    ///         <description>positive proposition atom (single letter where
    ///         <c>NAME</c> holds). <c>NAME</c> is auto-registered in the
    ///         supplied <see cref="ConditionRegistry{TPred}"/>.</description></item>
    ///   <item><term><c>(nprop NAME)</c></term>
    ///         <description>negative proposition atom (single letter where
    ///         <c>NAME</c> does <em>not</em> hold).</description></item>
    ///   <item><term><c>(exists NAME r)</c></term>
    ///         <description>existential projection <c>∃NAME. r</c>
    ///         (<see cref="EreExists{TPred}"/>).</description></item>
    /// </list>
    ///
    /// <para>This module is the shared-DSL surface used for cross-validation
    /// against the Rust <c>resharp-algebra</c> implementation (EREQ Phase 5):
    /// the same printer feeds both BraggerSpecs and the Rust shim binary
    /// at <c>Rust/src/ereq/src/bin/ereq-emptiness.rs</c>, so emptiness
    /// verdicts can be compared structurally on identical inputs.</para>
    ///
    /// <para>The base ERE forms (<c>empty</c>, <c>eps</c>, <c>(atom …)</c>,
    /// <c>(concat …)</c>, <c>(union …)</c>, <c>(inter …)</c>, <c>(star r)</c>,
    /// <c>(comp r)</c>, <c>(fusion r1 r2)</c>, <c>(xor …)</c>, <c>(xnor …)</c>)
    /// are delegated to <see cref="RltlSExpr"/> so this module stays a thin
    /// extension and round-trips with the existing tests.</para>
    /// </summary>
    public static class EreqSExpr
    {
        // ─────────────────────────────────────────────────────────────────
        // ERE+EREQ → SExpr
        // ─────────────────────────────────────────────────────────────────

        public static SExpr ToSExpr<TPred>(
            Ere<TPred> ere,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
        {
            if (ere == null) throw new ArgumentNullException(nameof(ere));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return EreqTo(ere, codec, registry);
        }

        public static string Print<TPred>(
            Ere<TPred> ere,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
            => ToSExpr(ere, codec, registry).ToString();

        private static SExpr EreqTo<TPred>(
            Ere<TPred> ere,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
        {
            switch (ere)
            {
                case EreEmpty<TPred> _:    return new SAtom("empty");
                case EreEpsilon<TPred> _:  return new SAtom("eps");
                case EreAtom<TPred> a:
                    return new SList(new SAtom("atom"), codec.Print(a.Predicate));
                case EreProposition<TPred> p:
                    return new SList(
                        new SAtom(p.Polarity ? "prop" : "nprop"),
                        new SAtom(registry.GetPropositionName(p.PropositionIndex)));
                case EreExists<TPred> ex:
                    return new SList(
                        new SAtom("exists"),
                        new SAtom(registry.GetPropositionName(ex.PropositionIndex)),
                        EreqTo(ex.Body, codec, registry));
                case EreConcat<TPred> c:
                    {
                        var ops = new List<Ere<TPred>>();
                        FlattenConcat(c, ops);
                        return Nary("concat", ops, codec, registry);
                    }
                case EreUnion<TPred> u:
                    return Nary("union", u.Operands, codec, registry);
                case EreIntersect<TPred> i:
                    return Nary("inter", i.Operands, codec, registry);
                case EreComplement<TPred> n:
                    return new SList(new SAtom("comp"), EreqTo(n.Inner, codec, registry));
                case EreStar<TPred> s:
                    return new SList(new SAtom("star"), EreqTo(s.Inner, codec, registry));
                case EreFusion<TPred> f:
                    return new SList(new SAtom("fusion"),
                        EreqTo(f.Left, codec, registry),
                        EreqTo(f.Right, codec, registry));
                case EreXor<TPred> x:
                    {
                        var items = new SExpr[x.Operands.Count + 1];
                        items[0] = new SAtom(x.Negated ? "xnor" : "xor");
                        for (int j = 0; j < x.Operands.Count; j++)
                            items[j + 1] = EreqTo(x.Operands[j], codec, registry);
                        return new SList(items);
                    }
                default:
                    throw new ArgumentException(
                        $"Unknown ERE node type: {ere.GetType().Name}");
            }
        }

        private static void FlattenConcat<TPred>(Ere<TPred> e, List<Ere<TPred>> acc)
        {
            if (e is EreConcat<TPred> c) { FlattenConcat(c.Left, acc); FlattenConcat(c.Right, acc); }
            else acc.Add(e);
        }

        private static SExpr Nary<TPred>(
            string head,
            IReadOnlyList<Ere<TPred>> ops,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
        {
            var items = new SExpr[ops.Count + 1];
            items[0] = new SAtom(head);
            for (int i = 0; i < ops.Count; i++)
                items[i + 1] = EreqTo(ops[i], codec, registry);
            return new SList(items);
        }

        // ─────────────────────────────────────────────────────────────────
        // SExpr → ERE+EREQ
        // ─────────────────────────────────────────────────────────────────

        public static Ere<TPred> FromSExpr<TPred>(
            SExpr expr,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
        {
            if (expr == null) throw new ArgumentNullException(nameof(expr));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return ParseEreq(expr, codec, registry);
        }

        public static Ere<TPred> Parse<TPred>(
            string text,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
            => FromSExpr(SExpr.Parse(text), codec, registry);

        private static Ere<TPred> ParseEreq<TPred>(
            SExpr expr,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry)
        {
            if (expr is SAtom a)
            {
                switch (a.Value)
                {
                    case "empty": return EreEmpty<TPred>.Instance;
                    case "eps":   return EreEpsilon<TPred>.Instance;
                    default:
                        throw new FormatException(
                            $"Unknown ERE atom '{a.Value}'. Expected 'empty', 'eps', " +
                            "or a parenthesised form.");
                }
            }
            var list = (SList)expr;
            if (list.Items.Count == 0)
                throw new FormatException("Empty list is not a valid ERE form.");
            var head = ((SAtom)list.Items[0]).Value;
            switch (head)
            {
                case "atom":
                    Expect(list, 2, "atom");
                    return Ere<TPred>.Atom(codec.Parse(list.Items[1]));
                case "prop":
                case "nprop":
                    Expect(list, 2, head);
                    {
                        var name = ((SAtom)list.Items[1]).Value;
                        int idx = registry.RegisterProposition(name);
                        return Ere<TPred>.PropositionAtom(idx, polarity: head == "prop");
                    }
                case "exists":
                    Expect(list, 3, "exists");
                    {
                        var name = ((SAtom)list.Items[1]).Value;
                        int idx = registry.RegisterProposition(name);
                        var body = ParseEreq<TPred>(list.Items[2], codec, registry);
                        return Ere<TPred>.Exists(idx, body);
                    }
                case "concat":
                    return FoldBinary(list, codec, registry, Ere<TPred>.Concat, minArity: 1);
                case "union":
                    return FoldBinary(list, codec, registry, Ere<TPred>.Union, minArity: 1);
                case "inter":
                    return FoldBinary(list, codec, registry, Ere<TPred>.Intersect, minArity: 1);
                case "comp":
                    Expect(list, 2, "comp");
                    return Ere<TPred>.Complement(ParseEreq<TPred>(list.Items[1], codec, registry));
                case "star":
                    Expect(list, 2, "star");
                    return Ere<TPred>.Star(ParseEreq<TPred>(list.Items[1], codec, registry));
                case "fusion":
                    Expect(list, 3, "fusion");
                    return Ere<TPred>.Fusion(
                        ParseEreq<TPred>(list.Items[1], codec, registry),
                        ParseEreq<TPred>(list.Items[2], codec, registry));
                case "xor":
                    return FoldBinary(list, codec, registry, Ere<TPred>.Xor, minArity: 2);
                case "xnor":
                    return FoldBinary(list, codec, registry, Ere<TPred>.Xnor, minArity: 2);
                default:
                    throw new FormatException($"Unknown ERE/EREQ head '{head}'.");
            }
        }

        private static Ere<TPred> FoldBinary<TPred>(
            SList list,
            IPredicateCodec<TPred> codec,
            ConditionRegistry<TPred> registry,
            Func<Ere<TPred>, Ere<TPred>, Ere<TPred>> combine,
            int minArity)
        {
            int arity = list.Items.Count - 1;
            if (arity < minArity)
                throw new FormatException(
                    $"'{((SAtom)list.Items[0]).Value}' expects at least {minArity} operand(s), got {arity}.");
            var acc = ParseEreq<TPred>(list.Items[1], codec, registry);
            for (int i = 2; i < list.Items.Count; i++)
                acc = combine(acc, ParseEreq<TPred>(list.Items[i], codec, registry));
            return acc;
        }

        private static void Expect(SList list, int expected, string head)
        {
            if (list.Items.Count != expected)
                throw new FormatException(
                    $"'{head}' expects exactly {expected - 1} operand(s), got {list.Items.Count - 1}.");
        }
    }
}
