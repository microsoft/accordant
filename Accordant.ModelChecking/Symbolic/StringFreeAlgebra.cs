namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    /// <summary>
    /// Free predicate algebra over strings: no simplification, boolean
    /// combinations are rendered structurally. Useful for the
    /// <see cref="LtlJson"/> serializer where predicates are opaque
    /// labels and we just need a syntactic <c>Not</c>/<c>And</c>/<c>Or</c>.
    /// </summary>
    public sealed class StringFreeAlgebra : IPredicateAlgebra<string>
    {
        public static readonly StringFreeAlgebra Instance = new StringFreeAlgebra();

        public string Top => "⊤";
        public string Bottom => "⊥";
        public string And(string a, string b) => $"({a} ∧ {b})";
        public string Or(string a, string b) => $"({a} ∨ {b})";
        public string Not(string a) => a.StartsWith("¬") ? a.Substring(1) : "¬" + a;
        public bool IsSatisfiable(string predicate) => predicate != "⊥";
    }
}
