namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// JSON serialization for <see cref="Ere{TPred}"/> over string
    /// predicates. Mirrors the <see cref="LtlJson"/> pattern: opaque
    /// predicate labels, structural shapes, smart-constructor parse path
    /// (which means deserialization re-runs all ERE simplifications).
    ///
    /// Format:
    /// <code>
    /// ∅                 → { "op": "Empty" }
    /// ε                 → { "op": "Epsilon" }
    /// atom p            → { "op": "Atom", "pred": "p" }
    /// r · s             → { "op": "Concat", "left": ..., "right": ... }
    /// r ∪ s ∪ ...       → { "op": "Union", "args": [...] }
    /// r ∩ s ∩ ...       → { "op": "Intersect", "args": [...] }
    /// r*                → { "op": "Star", "inner": ... }
    /// ~r                → { "op": "Complement", "inner": ... }
    /// r : s             → { "op": "Fusion", "left": ..., "right": ... }
    /// r ⊕ s ⊕ ...       → { "op": "Xor", "args": [...] }            (negated=false)
    /// ~(r ⊕ s ⊕ ...)    → { "op": "Xor", "args": [...], "neg": true }
    /// </code>
    /// Sugar (parse-only): Sigma, Plus, Optional, Xnor.
    /// </summary>
    public static class EreJson
    {
        #region Serialization

        public static string Serialize(Ere<string> regex)
        {
            if (regex == null) throw new ArgumentNullException(nameof(regex));
            var sb = new StringBuilder();
            SerializeCore(regex, sb, 0);
            return sb.ToString();
        }

        internal static void SerializeCore(Ere<string> regex, StringBuilder sb, int depth)
        {
            if (depth > 1000)
                throw new InvalidOperationException("Regex nesting too deep (>1000).");

            switch (regex)
            {
                case EreEmpty<string> _:
                    sb.Append("{\"op\":\"Empty\"}"); break;
                case EreEpsilon<string> _:
                    sb.Append("{\"op\":\"Epsilon\"}"); break;
                case EreAtom<string> a:
                    sb.Append("{\"op\":\"Atom\",\"pred\":");
                    JsonUtil.AppendString(sb, a.Predicate);
                    sb.Append('}');
                    break;
                case EreConcat<string> c:
                    sb.Append("{\"op\":\"Concat\",\"left\":");
                    SerializeCore(c.Left, sb, depth + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(c.Right, sb, depth + 1);
                    sb.Append('}');
                    break;
                case EreUnion<string> u:
                    AppendArrayOp(sb, "Union", u.Operands, depth);
                    break;
                case EreIntersect<string> i:
                    AppendArrayOp(sb, "Intersect", i.Operands, depth);
                    break;
                case EreStar<string> s:
                    sb.Append("{\"op\":\"Star\",\"inner\":");
                    SerializeCore(s.Inner, sb, depth + 1);
                    sb.Append('}');
                    break;
                case EreComplement<string> n:
                    sb.Append("{\"op\":\"Complement\",\"inner\":");
                    SerializeCore(n.Inner, sb, depth + 1);
                    sb.Append('}');
                    break;
                case EreFusion<string> f:
                    sb.Append("{\"op\":\"Fusion\",\"left\":");
                    SerializeCore(f.Left, sb, depth + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(f.Right, sb, depth + 1);
                    sb.Append('}');
                    break;
                case EreXor<string> x:
                    sb.Append("{\"op\":\"Xor\",\"args\":[");
                    for (int k = 0; k < x.Operands.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        SerializeCore(x.Operands[k], sb, depth + 1);
                    }
                    sb.Append(']');
                    if (x.Negated) sb.Append(",\"neg\":true");
                    sb.Append('}');
                    break;
                default:
                    throw new ArgumentException($"Unknown ERE type: {regex.GetType()}");
            }
        }

        private static void AppendArrayOp(StringBuilder sb, string op,
            IReadOnlyList<Ere<string>> args, int depth)
        {
            sb.Append("{\"op\":\""); sb.Append(op); sb.Append("\",\"args\":[");
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeCore(args[i], sb, depth + 1);
            }
            sb.Append("]}");
        }

        #endregion

        #region Deserialization

        public static Ere<string> Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int pos = 0;
            return ParseRegex(json, ref pos);
        }

        internal static Ere<string> ParseRegex(string json, ref int pos)
        {
            JsonUtil.SkipWhitespace(json, ref pos);
            JsonUtil.Expect(json, ref pos, '{');
            JsonUtil.SkipWhitespace(json, ref pos);
            var fields = JsonUtil.ParseFields(json, ref pos);

            if (!fields.TryGetValue("op", out var op))
                throw new FormatException("Missing 'op' field in ERE JSON.");

            switch (op)
            {
                case "Empty":   return Ere<string>.Empty();
                case "Epsilon": return Ere<string>.Epsilon();
                case "Sigma":   return Ere<string>.Sigma();
                case "Atom":
                {
                    if (!fields.TryGetValue("pred", out var pred))
                        throw new FormatException("Atom missing 'pred' field.");
                    return Ere<string>.Atom(pred);
                }
                case "Concat":
                {
                    var (l, r) = ParseBinary(fields, "Concat");
                    return Ere<string>.Concat(l, r);
                }
                case "Fusion":
                {
                    var (l, r) = ParseBinary(fields, "Fusion");
                    return Ere<string>.Fusion(l, r);
                }
                case "Union":
                {
                    var args = ParseArgs(fields, "Union");
                    var result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = Ere<string>.Union(result, args[i]);
                    return result;
                }
                case "Intersect":
                {
                    var args = ParseArgs(fields, "Intersect");
                    var result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = Ere<string>.Intersect(result, args[i]);
                    return result;
                }
                case "Xor":
                {
                    var args = ParseArgs(fields, "Xor");
                    var result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = Ere<string>.Xor(result, args[i]);
                    bool neg = fields.TryGetValue("neg", out var nv) && nv == "true";
                    return neg ? Ere<string>.Complement(result) : result;
                }
                case "Xnor":
                {
                    var args = ParseArgs(fields, "Xnor");
                    var result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = Ere<string>.Xor(result, args[i]);
                    return Ere<string>.Complement(result);
                }
                case "Star":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Star missing 'inner' field.");
                    int p = 0;
                    return Ere<string>.Star(ParseRegex(innerJson, ref p));
                }
                case "Complement":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Complement missing 'inner' field.");
                    int p = 0;
                    return Ere<string>.Complement(ParseRegex(innerJson, ref p));
                }
                case "Plus":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Plus missing 'inner' field.");
                    int p = 0;
                    return Ere<string>.Plus(ParseRegex(innerJson, ref p));
                }
                case "Optional":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Optional missing 'inner' field.");
                    int p = 0;
                    return Ere<string>.Optional(ParseRegex(innerJson, ref p));
                }
                default:
                    throw new FormatException($"Unknown ERE op: '{op}'.");
            }
        }

        private static (Ere<string>, Ere<string>) ParseBinary(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("left", out var leftJson))
                throw new FormatException($"{opName} missing 'left' field.");
            if (!fields.TryGetValue("right", out var rightJson))
                throw new FormatException($"{opName} missing 'right' field.");
            int p1 = 0, p2 = 0;
            return (ParseRegex(leftJson, ref p1), ParseRegex(rightJson, ref p2));
        }

        private static List<Ere<string>> ParseArgs(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("args", out var argsJson))
                throw new FormatException($"{opName} missing 'args' field.");
            var result = new List<Ere<string>>();
            int pos = 0;
            JsonUtil.SkipWhitespace(argsJson, ref pos);
            JsonUtil.Expect(argsJson, ref pos, '[');
            JsonUtil.SkipWhitespace(argsJson, ref pos);
            if (pos < argsJson.Length && argsJson[pos] != ']')
            {
                result.Add(ParseRegex(argsJson, ref pos));
                JsonUtil.SkipWhitespace(argsJson, ref pos);
                while (pos < argsJson.Length && argsJson[pos] == ',')
                {
                    pos++;
                    JsonUtil.SkipWhitespace(argsJson, ref pos);
                    result.Add(ParseRegex(argsJson, ref pos));
                    JsonUtil.SkipWhitespace(argsJson, ref pos);
                }
            }
            if (result.Count == 0)
                throw new FormatException($"{opName} must have at least one argument.");
            return result;
        }

        #endregion
    }
}
