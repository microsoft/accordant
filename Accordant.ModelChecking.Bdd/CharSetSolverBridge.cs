namespace Microsoft.Accordant.ModelChecking.Bdd
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Reflection wrapper around the in-box BDD package shipped inside
    /// <c>System.Text.RegularExpressions.dll</c> as
    /// <c>System.Text.RegularExpressions.Symbolic.CharSetSolver</c> (.NET 7+).
    ///
    /// <para>
    /// The non-backtracking symbolic regex engine carries a complete
    /// canonicalising BDD apply-with-memoisation package (Bryant 1986
    /// style). Reflection-unwrapping it lets us reuse it as a
    /// general-purpose propositional decision procedure: BDD identity
    /// decides equivalence in O(1), apply is time-proportional to the
    /// product of operand sizes, and there is no fixed variable cap.
    /// </para>
    ///
    /// <para>
    /// The bridge is a process-wide singleton. All public methods are
    /// synchronised with a single lock because <c>CharSetSolver</c> is
    /// not documented as thread-safe and the apply-cache it maintains
    /// internally would race under concurrent <c>ApplyBinaryOp</c> calls.
    /// </para>
    /// </summary>
    public sealed class CharSetSolverBridge
    {
        private static readonly Lazy<CharSetSolverBridge> _instance =
            new Lazy<CharSetSolverBridge>(() => new CharSetSolverBridge(),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The lazily-constructed singleton bridge.</summary>
        public static CharSetSolverBridge Instance => _instance.Value;

        // --- Reflected handles --------------------------------------------------
        private readonly object _solver;
        private readonly object _tt;
        private readonly object _ff;
        private readonly MethodInfo _not;
        private readonly MethodInfo _binOp;
        private readonly MethodInfo _mkBdd;
        private readonly FieldInfo _bddOrdinal;
        private readonly FieldInfo _bddOne;
        private readonly FieldInfo _bddZero;
        private readonly object _gate = new object();

        // The op-id constants used by CharSetSolver.ApplyBinaryOp. These
        // correspond to enum members declared inside the engine. We keep
        // them as ints to avoid taking a hard reference to the internal
        // enum type.
        private const int OrId  = 0;
        private const int AndId = 1;
        private const int XorId = 2;

        /// <summary>Canonical ⊤ BDD (full universe).</summary>
        public object Top => _tt;

        /// <summary>Canonical ⊥ BDD (empty set).</summary>
        public object Bottom => _ff;

        private CharSetSolverBridge()
        {
            var asm = typeof(Regex).Assembly;

            var solverType = asm.GetType(
                "System.Text.RegularExpressions.Symbolic.CharSetSolver")
                ?? throw new InvalidOperationException(
                    "Could not locate System.Text.RegularExpressions.Symbolic.CharSetSolver. " +
                    "The BDD backend requires .NET 7 or later.");

            var bddType = asm.GetType(
                "System.Text.RegularExpressions.Symbolic.BDD")
                ?? throw new InvalidOperationException(
                    "Could not locate System.Text.RegularExpressions.Symbolic.BDD.");

            var ctor = solverType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new InvalidOperationException(
                    "CharSetSolver has no parameterless constructor on this runtime.");
            _solver = ctor.Invoke(Array.Empty<object>());

            _not = solverType.GetMethod("Not",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CharSetSolver.Not not found.");
            _binOp = solverType.GetMethod("ApplyBinaryOp",
                       BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CharSetSolver.ApplyBinaryOp not found.");
            _mkBdd = solverType.GetMethod("GetOrCreateBDD",
                       BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("CharSetSolver.GetOrCreateBDD not found.");

            _tt = solverType.GetProperty("Full",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                  ?.GetValue(_solver)
                ?? throw new InvalidOperationException("CharSetSolver.Full not found.");
            _ff = solverType.GetProperty("Empty",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                  ?.GetValue(_solver)
                ?? throw new InvalidOperationException("CharSetSolver.Empty not found.");

            _bddOrdinal = bddType.GetField("Ordinal",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("BDD.Ordinal not found.");
            _bddOne = bddType.GetField("One",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("BDD.One not found.");
            _bddZero = bddType.GetField("Zero",
                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("BDD.Zero not found.");
        }

        /// <summary>Create the BDD representing the single variable with
        /// the given ordinal.</summary>
        public object MkVar(int ordinal)
        {
            lock (_gate)
                return _mkBdd.Invoke(_solver, new object[] { ordinal, _tt, _ff });
        }

        /// <summary>Boolean complement.</summary>
        public object Not(object bdd)
        {
            lock (_gate)
                return _not.Invoke(_solver, new[] { bdd });
        }

        /// <summary>Boolean conjunction (canonicalising).</summary>
        public object And(object a, object b)
        {
            lock (_gate)
                return _binOp.Invoke(_solver, new[] { (object)AndId, a, b });
        }

        /// <summary>Boolean disjunction (canonicalising).</summary>
        public object Or(object a, object b)
        {
            lock (_gate)
                return _binOp.Invoke(_solver, new[] { (object)OrId, a, b });
        }

        /// <summary>Boolean xor — convenient for equivalence via
        /// <c>Xor(a,b) == ⊥</c>.</summary>
        public object Xor(object a, object b)
        {
            lock (_gate)
                return _binOp.Invoke(_solver, new[] { (object)XorId, a, b });
        }

        /// <summary>Reference-equality (≡ semantic equality, since the BDD
        /// package is canonicalising) check against ⊥.</summary>
        public bool IsFalse(object bdd) => ReferenceEquals(bdd, _ff);

        /// <summary>Reference-equality check against ⊤.</summary>
        public bool IsTrue(object bdd) => ReferenceEquals(bdd, _tt);

        /// <summary>Reference-equality of two BDDs — equivalent to
        /// semantic equivalence under the canonicalisation invariant.</summary>
        public bool AreSame(object a, object b) => ReferenceEquals(a, b);

        /// <summary>
        /// Walk a satisfying path of <paramref name="bdd"/> from the root
        /// down to ⊤, recording the variable assignments along the way.
        /// Returns an empty dictionary when <paramref name="bdd"/> is ⊤;
        /// throws when it is ⊥. Variables not appearing on the chosen
        /// path are left unassigned (don't care).
        /// </summary>
        public IReadOnlyDictionary<int, bool> ExtractModel(object bdd)
        {
            if (bdd == null) throw new ArgumentNullException(nameof(bdd));
            if (IsFalse(bdd))
                throw new ArgumentException(
                    "Cannot extract a model from ⊥.", nameof(bdd));

            var sol = new Dictionary<int, bool>();
            var node = bdd;
            while (!IsTrue(node))
            {
                var ordinal = (int)_bddOrdinal.GetValue(node);
                var oneBranch = _bddOne.GetValue(node);
                var zeroBranch = _bddZero.GetValue(node);

                // Prefer the branch that doesn't lead immediately to ⊥;
                // if both are non-⊥ we pick the 0-branch arbitrarily to
                // keep the assignment minimal.
                if (!IsFalse(zeroBranch))
                {
                    sol[ordinal] = false;
                    node = zeroBranch;
                }
                else
                {
                    sol[ordinal] = true;
                    node = oneBranch;
                }
            }
            return sol;
        }
    }
}
