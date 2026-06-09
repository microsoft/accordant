namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// JSON serialization for <see cref="Ltl{TPred}"/> formulas where
    /// predicates are strings (proposition names).
    /// 
    /// Format:
    /// <code>
    /// true              → { "op": "True" }
    /// false             → { "op": "False" }
    /// atom p            → { "op": "Atom", "pred": "p" }
    /// negated atom ¬p   → { "op": "Atom", "pred": "p", "neg": true }
    /// X φ               → { "op": "Next", "inner": ... }
    /// φ U ψ             → { "op": "Until", "left": ..., "right": ... }
    /// φ R ψ             → { "op": "Release", "left": ..., "right": ... }
    /// φ ∧ ψ ∧ ...       → { "op": "And", "args": [...] }
    /// φ ∨ ψ ∨ ...       → { "op": "Or", "args": [...] }
    /// </code>
    /// 
    /// Sugar forms (deserialization only accepts canonical, but these
    /// produce the correct formula):
    /// <code>
    /// F φ               → { "op": "Eventually", "inner": ... }  → ⊤ U φ
    /// G φ               → { "op": "Globally", "inner": ... }    → ⊥ R φ
    /// φ → ψ             → { "op": "Implies", "left": ..., "right": ... } → ¬φ ∨ ψ
    /// </code>
    /// </summary>
    public static class LtlJson
    {
        private static readonly LtlAlgebra<string> StringAlgebra =
            new LtlAlgebra<string>(StringFreeAlgebra.Instance);

        #region Serialization

        /// <summary>
        /// Serializes an LTL formula to JSON string.
        /// </summary>
        public static string Serialize(Ltl<string> formula)
        {
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            var sb = new StringBuilder();
            SerializeCore(formula, sb, 0);
            return sb.ToString();
        }

        /// <summary>
        /// Serializes with indentation for readability.
        /// </summary>
        public static string SerializePretty(Ltl<string> formula)
        {
            if (formula == null) throw new ArgumentNullException(nameof(formula));
            var sb = new StringBuilder();
            SerializeCore(formula, sb, 0, pretty: true, indent: 0);
            return sb.ToString();
        }

        private static void SerializeCore(Ltl<string> formula, StringBuilder sb, int depth,
            bool pretty = false, int indent = 0)
        {
            if (depth > 1000)
                throw new InvalidOperationException("Formula nesting too deep (>1000).");

            switch (formula)
            {
                case LtlTrue<string> _:
                    sb.Append("{\"op\":\"True\"}");
                    break;

                case LtlFalse<string> _:
                    sb.Append("{\"op\":\"False\"}");
                    break;

                case LtlAtom<string> atom:
                    sb.Append("{\"op\":\"Atom\",\"pred\":");
                    AppendJsonString(sb, atom.Predicate);
                    sb.Append('}');
                    break;

                case LtlNext<string> next:
                    sb.Append("{\"op\":\"Next\",\"inner\":");
                    SerializeCore(next.Inner, sb, depth + 1, pretty, indent + 1);
                    sb.Append('}');
                    break;

                case LtlUntil<string> until:
                    sb.Append("{\"op\":\"Until\",\"left\":");
                    SerializeCore(until.Left, sb, depth + 1, pretty, indent + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(until.Right, sb, depth + 1, pretty, indent + 1);
                    sb.Append('}');
                    break;

                case LtlRelease<string> release:
                    sb.Append("{\"op\":\"Release\",\"left\":");
                    SerializeCore(release.Left, sb, depth + 1, pretty, indent + 1);
                    sb.Append(",\"right\":");
                    SerializeCore(release.Right, sb, depth + 1, pretty, indent + 1);
                    sb.Append('}');
                    break;

                case LtlAnd<string> and:
                    sb.Append("{\"op\":\"And\",\"args\":[");
                    for (int i = 0; i < and.Operands.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        SerializeCore(and.Operands[i], sb, depth + 1, pretty, indent + 1);
                    }
                    sb.Append("]}");
                    break;

                case LtlOr<string> or:
                    sb.Append("{\"op\":\"Or\",\"args\":[");
                    for (int i = 0; i < or.Operands.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        SerializeCore(or.Operands[i], sb, depth + 1, pretty, indent + 1);
                    }
                    sb.Append("]}");
                    break;

                default:
                    throw new ArgumentException($"Unknown formula type: {formula.GetType()}");
            }
        }

        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }

        #endregion

        #region Deserialization

        /// <summary>
        /// Deserializes a JSON string to an LTL formula.
        /// Supports both canonical forms and sugar (Eventually, Globally, Implies).
        /// </summary>
        public static Ltl<string> Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int pos = 0;
            var result = ParseFormula(json, ref pos);
            return result;
        }

        private static Ltl<string> ParseFormula(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            Expect(json, ref pos, '{');
            SkipWhitespace(json, ref pos);

            var fields = ParseFields(json, ref pos);

            if (!fields.TryGetValue("op", out var op))
                throw new FormatException("Missing 'op' field in LTL JSON.");

            switch (op)
            {
                case "True":
                    return Ltl<string>.True();

                case "False":
                    return Ltl<string>.False();

                case "Atom":
                {
                    if (!fields.TryGetValue("pred", out var pred))
                        throw new FormatException("Atom missing 'pred' field.");
                    bool neg = fields.TryGetValue("neg", out var negVal) && negVal == "true";
                    return neg ? StringAlgebra.NegAtom(pred) : StringAlgebra.Atom(pred);
                }

                case "Next":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Next missing 'inner' field.");
                    int p = 0;
                    return Ltl<string>.Next(ParseFormula(innerJson, ref p));
                }

                case "Until":
                {
                    var (left, right) = ParseBinary(fields, "Until");
                    return Ltl<string>.Until(left, right);
                }

                case "Release":
                {
                    var (left, right) = ParseBinary(fields, "Release");
                    return Ltl<string>.Release(left, right);
                }

                case "And":
                {
                    var args = ParseArgs(fields, "And");
                    Ltl<string> result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = StringAlgebra.And(result, args[i]);
                    return result;
                }

                case "Or":
                {
                    var args = ParseArgs(fields, "Or");
                    Ltl<string> result = args[0];
                    for (int i = 1; i < args.Count; i++)
                        result = StringAlgebra.Or(result, args[i]);
                    return result;
                }

                // Sugar forms
                case "Eventually":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Eventually missing 'inner' field.");
                    int p = 0;
                    return Ltl<string>.Eventually(ParseFormula(innerJson, ref p));
                }

                case "Globally":
                {
                    if (!fields.TryGetValue("inner", out var innerJson))
                        throw new FormatException("Globally missing 'inner' field.");
                    int p = 0;
                    return Ltl<string>.Globally(ParseFormula(innerJson, ref p));
                }

                case "Implies":
                {
                    var (left, right) = ParseBinary(fields, "Implies");
                    return StringAlgebra.Implies(left, right);
                }

                default:
                    throw new FormatException($"Unknown op: '{op}'.");
            }
        }

        private static (Ltl<string>, Ltl<string>) ParseBinary(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("left", out var leftJson))
                throw new FormatException($"{opName} missing 'left' field.");
            if (!fields.TryGetValue("right", out var rightJson))
                throw new FormatException($"{opName} missing 'right' field.");
            int p1 = 0, p2 = 0;
            return (ParseFormula(leftJson, ref p1), ParseFormula(rightJson, ref p2));
        }

        private static List<Ltl<string>> ParseArgs(
            Dictionary<string, string> fields, string opName)
        {
            if (!fields.TryGetValue("args", out var argsJson))
                throw new FormatException($"{opName} missing 'args' field.");
            var result = new List<Ltl<string>>();
            int pos = 0;
            SkipWhitespace(argsJson, ref pos);
            Expect(argsJson, ref pos, '[');
            SkipWhitespace(argsJson, ref pos);
            if (pos < argsJson.Length && argsJson[pos] != ']')
            {
                result.Add(ParseFormula(argsJson, ref pos));
                SkipWhitespace(argsJson, ref pos);
                while (pos < argsJson.Length && argsJson[pos] == ',')
                {
                    pos++;
                    SkipWhitespace(argsJson, ref pos);
                    result.Add(ParseFormula(argsJson, ref pos));
                    SkipWhitespace(argsJson, ref pos);
                }
            }
            if (result.Count == 0)
                throw new FormatException($"{opName} must have at least one argument.");
            return result;
        }

        /// <summary>
        /// Simple JSON object field parser. Returns field name → raw JSON value string.
        /// Handles nested objects/arrays by tracking brace/bracket depth.
        /// </summary>
        private static Dictionary<string, string> ParseFields(string json, ref int pos)
        {
            var fields = new Dictionary<string, string>();
            SkipWhitespace(json, ref pos);

            while (pos < json.Length && json[pos] != '}')
            {
                if (json[pos] == ',') { pos++; SkipWhitespace(json, ref pos); continue; }

                // Parse field name
                var name = ParseJsonString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                SkipWhitespace(json, ref pos);

                // Parse field value (capture raw JSON)
                var value = CaptureValue(json, ref pos);
                fields[name] = value;
                SkipWhitespace(json, ref pos);
            }

            if (pos < json.Length) pos++; // consume '}'
            return fields;
        }

        private static string CaptureValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            char c = json[pos];

            if (c == '"')
            {
                return ParseJsonString(json, ref pos);
            }
            else if (c == '{' || c == '[')
            {
                int start = pos;
                int depth = 0;
                bool inString = false;
                while (pos < json.Length)
                {
                    char ch = json[pos];
                    if (inString)
                    {
                        if (ch == '\\') { pos++; } // skip escaped char
                        else if (ch == '"') { inString = false; }
                    }
                    else
                    {
                        if (ch == '"') inString = true;
                        else if (ch == '{' || ch == '[') depth++;
                        else if (ch == '}' || ch == ']')
                        {
                            depth--;
                            if (depth == 0) { pos++; break; }
                        }
                    }
                    pos++;
                }
                return json.Substring(start, pos - start);
            }
            else if (c == 't' || c == 'f')
            {
                // true or false
                int start = pos;
                while (pos < json.Length && char.IsLetter(json[pos])) pos++;
                return json.Substring(start, pos - start);
            }
            else if (c == 'n')
            {
                // null
                int start = pos;
                while (pos < json.Length && char.IsLetter(json[pos])) pos++;
                return json.Substring(start, pos - start);
            }
            else if (char.IsDigit(c) || c == '-')
            {
                int start = pos;
                while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '.' || json[pos] == '-' || json[pos] == 'e' || json[pos] == 'E'))
                    pos++;
                return json.Substring(start, pos - start);
            }
            else
            {
                throw new FormatException($"Unexpected character '{c}' at position {pos}.");
            }
        }

        private static string ParseJsonString(string json, ref int pos)
        {
            Expect(json, ref pos, '"');
            var sb = new StringBuilder();
            while (pos < json.Length && json[pos] != '"')
            {
                if (json[pos] == '\\')
                {
                    pos++;
                    switch (json[pos])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '/': sb.Append('/'); break;
                        default: sb.Append(json[pos]); break;
                    }
                }
                else
                {
                    sb.Append(json[pos]);
                }
                pos++;
            }
            Expect(json, ref pos, '"');
            return sb.ToString();
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }

        private static void Expect(string json, ref int pos, char expected)
        {
            if (pos >= json.Length || json[pos] != expected)
                throw new FormatException(
                    $"Expected '{expected}' at position {pos}, got '{(pos < json.Length ? json[pos].ToString() : "EOF")}'.");
            pos++;
        }

        #endregion
    }
}
