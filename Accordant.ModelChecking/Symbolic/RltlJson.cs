namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// JSON serialization for <see cref="Rltl{TPred}"/> over string
    /// predicates. Mirrors <see cref="LtlJson"/> and extends it with the
    /// RLTL-specific embedded-regex constructors that delegate to
    /// <see cref="EreJson"/>.
    ///
    /// Format (superset of <see cref="LtlJson"/>):
    /// <code>
    /// SeqPrefix R;φ         → { "op": "SeqPrefix",      "regex": &lt;ERE-JSON&gt;, "phi": ... }
    /// OvlPrefix R:φ         → { "op": "OvlPrefix",      "regex": &lt;ERE-JSON&gt;, "phi": ... }
    /// Trigger   R⊳φ         → { "op": "Trigger",        "regex": &lt;ERE-JSON&gt;, "phi": ... }
    /// Match     R⊳⊳φ        → { "op": "Match",          "regex": &lt;ERE-JSON&gt;, "phi": ... }
    /// WeakClosure {R}       → { "op": "WeakClosure",    "regex": &lt;ERE-JSON&gt; }
    /// NegWeakClosure {{R}}̄ → { "op": "NegWeakClosure", "regex": &lt;ERE-JSON&gt; }
    /// OmegaClosure {R}ω     → { "op": "OmegaClosure",   "regex": &lt;ERE-JSON&gt; }
    /// </code>
    /// Predicates are opaque strings; deserialization re-runs RLTL's smart
    /// constructors via <see cref="RltlAlgebra{TPred}"/> with
    /// <see cref="StringFreeAlgebra"/>.
    /// </summary>
    public static class RltlJson
    {
        private static readonly RltlAlgebra<string> StringAlgebra =
            new RltlAlgebra<string>(StringFreeAlgebra.Instance);

        #region Serialization

        public static string Serialize(Rltl<string> formula)
        {
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            var sb = new StringBuilder();
            SerializeCore(formula, sb, 0);
            return sb.ToString();
        }

        private static void SerializeCore(Rltl<string> f, StringBuilder sb, int depth)
        {
            if (depth > 1000)
                throw new InvalidOperationException("Formula nesting too deep (>1000).");

            switch (f)
            {
                case RltlTrue<string> _:  sb.Append("{\"op\":\"True\"}"); break;
                case RltlFalse<string> _: sb.Append("{\"op\":\"False\"}"); break;
                case RltlAtom<string> a:
                    sb.Append("{\"op\":\"Atom\",\"pred\":");
                    JsonUtil.AppendString(sb, a.Predicate);
                    sb.Append('}');
                    break;
                case RltlNext<string> n:
                    sb.Append("{\"op\":\"Next\",\"inner\":");
                    SerializeCore(n.Inner, sb, depth + 1);
                    sb.Append('}');
                    break;
                case RltlUntil<string> u:
                    sb.Append("{\"op\":\"Until\",\"left\":");
                    SerializeCore(u.Left, sb, depth + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(u.Right, sb, depth + 1);
                    sb.Append('}');
                    break;
                case RltlRelease<string> r:
                    sb.Append("{\"op\":\"Release\",\"left\":");
                    SerializeCore(r.Left, sb, depth + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(r.Right, sb, depth + 1);
                    sb.Append('}');
                    break;
                case RltlAnd<string> a:
                    sb.Append("{\"op\":\"And\",\"args\":[");
                    for (int i = 0; i < a.Operands.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        SerializeCore(a.Operands[i], sb, depth + 1);
                    }
                    sb.Append("]}");
                    break;
                case RltlOr<string> o:
                    sb.Append("{\"op\":\"Or\",\"args\":[");
                    for (int i = 0; i < o.Operands.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        SerializeCore(o.Operands[i], sb, depth + 1);
                    }
                    sb.Append("]}");
                    break;
                case RltlSeqPrefix<string> s: EmitRegexAndPhi(sb, "SeqPrefix", s.Regex, s.Phi, depth); break;
                case RltlOvlPrefix<string> s: EmitRegexAndPhi(sb, "OvlPrefix", s.Regex, s.Phi, depth); break;
                case RltlTrigger<string>   s: EmitRegexAndPhi(sb, "Trigger",   s.Regex, s.Phi, depth); break;
                case RltlMatch<string>     s: EmitRegexAndPhi(sb, "Match",     s.Regex, s.Phi, depth); break;
                case RltlWeakClosure<string>    w: EmitRegexOnly(sb, "WeakClosure",    w.Regex, depth); break;
                case RltlNegWeakClosure<string> w: EmitRegexOnly(sb, "NegWeakClosure", w.Regex, depth); break;
                case RltlOmegaClosure<string>   w: EmitRegexOnly(sb, "OmegaClosure",   w.Regex, depth); break;
                default:
                    throw new ArgumentException($"Unknown RLTL type: {f.GetType()}");
            }
        }

        private static void EmitRegexAndPhi(StringBuilder sb, string op,
            Ere<string> regex, Rltl<string> phi, int depth)
        {
            sb.Append("{\"op\":\""); sb.Append(op); sb.Append("\",\"regex\":");
            EreJson.SerializeCore(regex, sb, depth + 1);
            sb.Append(",\"phi\":");
            SerializeCore(phi, sb, depth + 1);
            sb.Append('}');
        }

        private static void EmitRegexOnly(StringBuilder sb, string op,
            Ere<string> regex, int depth)
        {
            sb.Append("{\"op\":\""); sb.Append(op); sb.Append("\",\"regex\":");
            EreJson.SerializeCore(regex, sb, depth + 1);
            sb.Append('}');
        }

        #endregion

        #region Deserialization

        public static Rltl<string> Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int pos = 0;
            return ParseFormula(json, ref pos);
        }

        private static Rltl<string> ParseFormula(string json, ref int pos)
        {
            JsonUtil.SkipWhitespace(json, ref pos);
            JsonUtil.Expect(json, ref pos, '{');
            JsonUtil.SkipWhitespace(json, ref pos);
            var fields = JsonUtil.ParseFields(json, ref pos);

            if (!fields.TryGetValue("op", out var op))
                throw new FormatException("Missing 'op' field in RLTL JSON.");

            switch (op)
            {
                case "True":  return StringAlgebra.True;
                case "False": return StringAlgebra.False;
                case "Atom":
                {
                    if (!fields.TryGetValue("pred", out var pred))
                        throw new FormatException("Atom missing 'pred' field.");
                    bool neg = fields.TryGetValue("neg", out var nv) && nv == "true";
                    return neg ? StringAlgebra.NegAtom(pred) : StringAlgebra.Atom(pred);
                }
                case "Next":
                {
                    var inner = ParseInner(fields, "Next");
                    return StringAlgebra.Next(inner);
                }
                case "Until":
                {
                    var (l, r) = ParseBinary(fields, "Until");
                    return StringAlgebra.Until(l, r);
                }
                case "Release":
                {
                    var (l, r) = ParseBinary(fields, "Release");
                    return StringAlgebra.Release(l, r);
                }
                case "Eventually": return StringAlgebra.Eventually(ParseInner(fields, "Eventually"));
                case "Globally":   return StringAlgebra.Globally(ParseInner(fields, "Globally"));
                case "And":
                {
                    var args = ParseArgs(fields, "And");
                    var r = args[0];
                    for (int i = 1; i < args.Count; i++) r = StringAlgebra.And(r, args[i]);
                    return r;
                }
                case "Or":
                {
                    var args = ParseArgs(fields, "Or");
                    var r = args[0];
                    for (int i = 1; i < args.Count; i++) r = StringAlgebra.Or(r, args[i]);
                    return r;
                }
                case "Implies":
                {
                    var (l, r) = ParseBinary(fields, "Implies");
                    return StringAlgebra.Or(StringAlgebra.Not(l), r);
                }
                case "SeqPrefix": return StringAlgebra.SeqPrefix(ParseRegex(fields, "SeqPrefix"), ParsePhi(fields, "SeqPrefix"));
                case "OvlPrefix": return StringAlgebra.OvlPrefix(ParseRegex(fields, "OvlPrefix"), ParsePhi(fields, "OvlPrefix"));
                case "Trigger":   return StringAlgebra.Trigger(ParseRegex(fields, "Trigger"),     ParsePhi(fields, "Trigger"));
                case "Match":     return StringAlgebra.Match(ParseRegex(fields, "Match"),         ParsePhi(fields, "Match"));
                case "WeakClosure":    return StringAlgebra.WeakClosure(ParseRegex(fields, "WeakClosure"));
                case "NegWeakClosure": return StringAlgebra.NegWeakClosure(ParseRegex(fields, "NegWeakClosure"));
                case "OmegaClosure":   return StringAlgebra.OmegaClosure(ParseRegex(fields, "OmegaClosure"));
                default:
                    throw new FormatException($"Unknown RLTL op: '{op}'.");
            }
        }

        private static Rltl<string> ParseInner(Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("inner", out var j))
                throw new FormatException($"{opName} missing 'inner' field.");
            int p = 0;
            return ParseFormula(j, ref p);
        }

        private static (Rltl<string>, Rltl<string>) ParseBinary(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("left", out var leftJson))
                throw new FormatException($"{opName} missing 'left' field.");
            if (!fields.TryGetValue("right", out var rightJson))
                throw new FormatException($"{opName} missing 'right' field.");
            int p1 = 0, p2 = 0;
            return (ParseFormula(leftJson, ref p1), ParseFormula(rightJson, ref p2));
        }

        private static List<Rltl<string>> ParseArgs(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("args", out var argsJson))
                throw new FormatException($"{opName} missing 'args' field.");
            var result = new List<Rltl<string>>();
            int pos = 0;
            JsonUtil.SkipWhitespace(argsJson, ref pos);
            JsonUtil.Expect(argsJson, ref pos, '[');
            JsonUtil.SkipWhitespace(argsJson, ref pos);
            if (pos < argsJson.Length && argsJson[pos] != ']')
            {
                result.Add(ParseFormula(argsJson, ref pos));
                JsonUtil.SkipWhitespace(argsJson, ref pos);
                while (pos < argsJson.Length && argsJson[pos] == ',')
                {
                    pos++;
                    JsonUtil.SkipWhitespace(argsJson, ref pos);
                    result.Add(ParseFormula(argsJson, ref pos));
                    JsonUtil.SkipWhitespace(argsJson, ref pos);
                }
            }
            if (result.Count == 0)
                throw new FormatException($"{opName} must have at least one argument.");
            return result;
        }

        private static Ere<string> ParseRegex(Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("regex", out var rJson))
                throw new FormatException($"{opName} missing 'regex' field.");
            int p = 0;
            return EreJson.ParseRegex(rJson, ref p);
        }

        private static Rltl<string> ParsePhi(Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("phi", out var pJson))
                throw new FormatException($"{opName} missing 'phi' field.");
            int p = 0;
            return ParseFormula(pJson, ref p);
        }

        #endregion
    }
}
