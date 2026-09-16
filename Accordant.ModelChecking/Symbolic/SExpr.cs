namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// S-expression abstract syntax. Either an atomic token
    /// (<see cref="SAtom"/>) or a parenthesised list of sub-expressions
    /// (<see cref="SList"/>). The shared base supports a simple writer-style
    /// printer; richer formatting lives in <see cref="SExprPrinter"/>.
    /// </summary>
    public abstract class SExpr
    {
        /// <summary>Parses an S-expression from a string. Throws <see cref="FormatException"/>.</summary>
        public static SExpr Parse(string text) => SExprParser.Parse(text);

        /// <summary>Single-line canonical print.</summary>
        public override string ToString() => SExprPrinter.Print(this);

        public abstract bool DeepEquals(SExpr other);
    }

    /// <summary>An atomic S-expression token (identifier, number, or quoted string).</summary>
    public sealed class SAtom : SExpr
    {
        /// <summary>The token text exactly as it should be emitted (unquoted form).</summary>
        public string Value { get; }
        /// <summary>When true the atom must be emitted as a quoted string literal.</summary>
        public bool Quoted { get; }

        public SAtom(string value, bool quoted = false)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Quoted = quoted || NeedsQuoting(value);
        }

        public override bool DeepEquals(SExpr other)
            => other is SAtom a && a.Value == Value && a.Quoted == Quoted;

        internal static bool NeedsQuoting(string s)
        {
            if (s.Length == 0) return true;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '"' || c == ';' || c == '\\')
                    return true;
            }
            return false;
        }
    }

    /// <summary>A parenthesised list of S-expressions.</summary>
    public sealed class SList : SExpr
    {
        public IReadOnlyList<SExpr> Items { get; }
        public SList(params SExpr[] items)
            : this((IReadOnlyList<SExpr>)items ?? throw new ArgumentNullException(nameof(items))) { }
        public SList(IReadOnlyList<SExpr> items)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }
        public override bool DeepEquals(SExpr other)
        {
            if (!(other is SList l) || l.Items.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++)
                if (!Items[i].DeepEquals(l.Items[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// Hand-written recursive-descent S-expression parser. Supports
    /// identifiers, signed integers, double-quoted strings with C-style
    /// escapes (<c>\\</c>, <c>\"</c>, <c>\n</c>, <c>\t</c>, <c>\r</c>),
    /// and semicolon-to-EOL comments.
    /// </summary>
    public static class SExprParser
    {
        public static SExpr Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int pos = 0;
            SkipWs(text, ref pos);
            var expr = ParseExpr(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length)
                throw new FormatException(
                    $"Unexpected trailing input at position {pos}: '{text.Substring(pos, Math.Min(20, text.Length - pos))}'");
            return expr;
        }

        /// <summary>Parses a sequence of S-expressions (zero or more).</summary>
        public static IReadOnlyList<SExpr> ParseMany(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var results = new List<SExpr>();
            int pos = 0;
            while (true)
            {
                SkipWs(text, ref pos);
                if (pos >= text.Length) break;
                results.Add(ParseExpr(text, ref pos));
            }
            return results;
        }

        private static SExpr ParseExpr(string text, ref int pos)
        {
            SkipWs(text, ref pos);
            if (pos >= text.Length)
                throw new FormatException("Unexpected end of input.");
            var c = text[pos];
            if (c == '(') return ParseList(text, ref pos);
            if (c == ')') throw new FormatException($"Unexpected ')' at position {pos}.");
            if (c == '"') return ParseString(text, ref pos);
            return ParseAtom(text, ref pos);
        }

        private static SList ParseList(string text, ref int pos)
        {
            pos++; // consume '('
            var items = new List<SExpr>();
            while (true)
            {
                SkipWs(text, ref pos);
                if (pos >= text.Length)
                    throw new FormatException("Unclosed '(': end of input.");
                if (text[pos] == ')') { pos++; return new SList(items); }
                items.Add(ParseExpr(text, ref pos));
            }
        }

        private static SAtom ParseString(string text, ref int pos)
        {
            pos++; // consume opening "
            var sb = new StringBuilder();
            while (pos < text.Length)
            {
                var c = text[pos++];
                if (c == '"') return new SAtom(sb.ToString(), quoted: true);
                if (c == '\\')
                {
                    if (pos >= text.Length)
                        throw new FormatException("Unterminated escape sequence at end of input.");
                    var esc = text[pos++];
                    switch (esc)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"':  sb.Append('"');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'r':  sb.Append('\r'); break;
                        default:   sb.Append(esc);  break;
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("Unterminated string literal.");
        }

        private static SAtom ParseAtom(string text, ref int pos)
        {
            int start = pos;
            while (pos < text.Length)
            {
                var c = text[pos];
                if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '"' || c == ';') break;
                pos++;
            }
            return new SAtom(text.Substring(start, pos - start));
        }

        private static void SkipWs(string text, ref int pos)
        {
            while (pos < text.Length)
            {
                var c = text[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }
                if (c == ';')
                {
                    while (pos < text.Length && text[pos] != '\n') pos++;
                    continue;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Canonical single-line printer. Atoms that contain whitespace, parens,
    /// quotes, semicolons or backslashes, or that are flagged
    /// <see cref="SAtom.Quoted"/>, are emitted as double-quoted strings with
    /// the inverse C-style escapes accepted by <see cref="SExprParser"/>.
    /// </summary>
    public static class SExprPrinter
    {
        public static string Print(SExpr expr)
        {
            var sb = new StringBuilder();
            PrintInto(expr, sb);
            return sb.ToString();
        }

        private static void PrintInto(SExpr expr, StringBuilder sb)
        {
            switch (expr)
            {
                case SAtom a:
                    if (a.Quoted) PrintQuoted(a.Value, sb);
                    else sb.Append(a.Value);
                    break;
                case SList l:
                    sb.Append('(');
                    for (int i = 0; i < l.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        PrintInto(l.Items[i], sb);
                    }
                    sb.Append(')');
                    break;
                default:
                    throw new ArgumentException("Unknown SExpr subtype.", nameof(expr));
            }
        }

        private static void PrintQuoted(string value, StringBuilder sb)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\"");  break;
                    case '\n': sb.Append("\\n");   break;
                    case '\t': sb.Append("\\t");   break;
                    case '\r': sb.Append("\\r");   break;
                    default:   sb.Append(c);       break;
                }
            }
            sb.Append('"');
        }
    }
}
