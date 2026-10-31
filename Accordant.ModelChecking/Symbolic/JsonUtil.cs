namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Shared JSON utility helpers used by both <see cref="LtlJson"/>-style
    /// and <see cref="EreJson"/>/<see cref="RltlJson"/> serializers.
    /// Extracted to remove duplication while keeping <see cref="LtlJson"/>
    /// API-stable (it still uses its own private copies; see file).
    /// </summary>
    internal static class JsonUtil
    {
        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:   sb.Append(c); break;
                }
            }
            sb.Append('"');
        }

        public static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }

        public static void Expect(string json, ref int pos, char expected)
        {
            if (pos >= json.Length || json[pos] != expected)
                throw new FormatException(
                    $"Expected '{expected}' at position {pos}, got '"
                    + (pos < json.Length ? json[pos].ToString() : "EOF") + "'.");
            pos++;
        }

        public static string ParseString(string json, ref int pos)
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
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case '/':  sb.Append('/'); break;
                        default:   sb.Append(json[pos]); break;
                    }
                }
                else { sb.Append(json[pos]); }
                pos++;
            }
            Expect(json, ref pos, '"');
            return sb.ToString();
        }

        public static Dictionary<string, string> ParseFields(string json, ref int pos)
        {
            var fields = new Dictionary<string, string>();
            SkipWhitespace(json, ref pos);
            while (pos < json.Length && json[pos] != '}')
            {
                if (json[pos] == ',') { pos++; SkipWhitespace(json, ref pos); continue; }
                var name = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                SkipWhitespace(json, ref pos);
                var value = CaptureValue(json, ref pos);
                fields[name] = value;
                SkipWhitespace(json, ref pos);
            }
            if (pos < json.Length) pos++; // consume '}'
            return fields;
        }

        public static string CaptureValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            char c = json[pos];
            if (c == '"') return ParseString(json, ref pos);
            if (c == '{' || c == '[')
            {
                int start = pos;
                int depth = 0;
                bool inString = false;
                while (pos < json.Length)
                {
                    char ch = json[pos];
                    if (inString)
                    {
                        if (ch == '\\') { pos++; }
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
            if (c == 't' || c == 'f' || c == 'n')
            {
                int start = pos;
                while (pos < json.Length && char.IsLetter(json[pos])) pos++;
                return json.Substring(start, pos - start);
            }
            if (char.IsDigit(c) || c == '-')
            {
                int start = pos;
                while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '.'
                    || json[pos] == '-' || json[pos] == 'e' || json[pos] == 'E')) pos++;
                return json.Substring(start, pos - start);
            }
            throw new FormatException($"Unexpected character '{c}' at position {pos}.");
        }
    }
}
