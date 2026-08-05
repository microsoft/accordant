namespace Microsoft.Accordant.ModelChecking.Symbolic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A named atomic proposition over model program states.
    /// Each proposition has a unique integer Id for identity/ordering
    /// and a Name for display. The evaluation function tests the proposition
    /// against a concrete <see cref="State"/>.
    /// </summary>
    public sealed class StateProp : IEquatable<StateProp>, IComparable<StateProp>
    {
        private static int _nextId;

        /// <summary>Unique identity for equality and ordering.</summary>
        public int Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Evaluation: tests whether the proposition holds in a state.</summary>
        public Func<State, bool> Evaluate { get; }

        public StateProp(string name, Func<State, bool> evaluate)
        {
            Id = System.Threading.Interlocked.Increment(ref _nextId);
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        }

        public bool Equals(StateProp other) => other != null && Id == other.Id;
        public override bool Equals(object obj) => Equals(obj as StateProp);
        public override int GetHashCode() => Id;
        public int CompareTo(StateProp other) => other == null ? 1 : Id.CompareTo(other.Id);
        public override string ToString() => Name;
    }

    /// <summary>
    /// Effective Boolean algebra over <see cref="StateProp"/> with
    /// <see cref="State"/> as the element type.
    /// 
    /// Compound predicates are represented structurally for proper equality
    /// in the condition registry. <see cref="IsSatisfiable"/> is precise for
    /// the propositional fragment built from <see cref="And"/>,
    /// <see cref="Or"/>, <see cref="Not"/> and <see cref="StatePredAtom"/>:
    /// each <see cref="StateProp"/> is treated as an independent Boolean
    /// variable and a brute-force truth-table decides the formula. Formulas
    /// with more than 20 distinct atoms fall back to a conservative
    /// <c>true</c> answer.
    /// </summary>
    public sealed class StatePropEba : IEffectiveBooleanAlgebra<IStatePredicate, State>
    {
        public static readonly StatePropEba Instance = new StatePropEba();

        public IStatePredicate Top => StatePredTrue.Instance;
        public IStatePredicate Bottom => StatePredFalse.Instance;

        public IStatePredicate And(IStatePredicate a, IStatePredicate b)
        {
            if (a is StatePredTrue) return b;
            if (b is StatePredTrue) return a;
            if (a is StatePredFalse || b is StatePredFalse) return Bottom;
            if (a.Equals(b)) return a;
            if (IsNegationOf(a, b) || IsNegationOf(b, a)) return Bottom;
            return new StatePredAnd(a, b);
        }

        public IStatePredicate Or(IStatePredicate a, IStatePredicate b)
        {
            if (a is StatePredFalse) return b;
            if (b is StatePredFalse) return a;
            if (a is StatePredTrue || b is StatePredTrue) return Top;
            if (a.Equals(b)) return a;
            if (IsNegationOf(a, b) || IsNegationOf(b, a)) return Top;
            return new StatePredOr(a, b);
        }

        /// <summary>
        /// Returns true iff <paramref name="neg"/> is structurally
        /// <c>¬<paramref name="pos"/></c>. Used to collapse complementary
        /// literals at the EBA level.
        /// </summary>
        private static bool IsNegationOf(IStatePredicate neg, IStatePredicate pos)
            => neg is StatePredNot n && n.Inner.Equals(pos);

        public IStatePredicate Not(IStatePredicate a)
        {
            if (a is StatePredTrue) return Bottom;
            if (a is StatePredFalse) return Top;
            if (a is StatePredNot neg) return neg.Inner;
            return new StatePredNot(a);
        }

        public bool IsSatisfiable(IStatePredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (predicate is StatePredFalse) return false;
            if (predicate is StatePredTrue) return true;
            if (predicate is StatePredAtom) return true;   // a single atom can always be made true
            if (predicate is StatePredNot n0 && n0.Inner is StatePredAtom) return true;

            // Propositional decision over the atomic StateProps occurring in
            // the formula. Each <see cref="StateProp"/> is treated as an
            // independent Boolean variable (its Evaluate callback is opaque
            // to the algebra), and brute-force enumerates 2^|atoms|
            // assignments. This is precise for the propositional fragment we
            // build via <see cref="And"/>/<see cref="Or"/>/<see cref="Not"/>
            // and detects contradictions such as <c>p ∧ ¬p</c>,
            // <c>(p ∧ q) ∧ ¬p</c>, etc.
            //
            // For larger formulas (more than <see cref="MaxAtomsForBruteForce"/>
            // distinct atoms) we fall back to the conservative answer
            // <c>true</c>, matching the previous behaviour and keeping the
            // common case fast.
            var atomIds = new List<int>();
            var seen = new HashSet<int>();
            CollectAtomIds(predicate, atomIds, seen);

            if (atomIds.Count == 0)
            {
                // No atoms — the formula reduces to a constant. Evaluate once
                // under an empty assignment.
                return EvalProp(predicate, null);
            }

            if (atomIds.Count > MaxAtomsForBruteForce)
                return true;

            int total = 1 << atomIds.Count;
            var assignment = new bool[atomIds.Count];
            var idToIndex = new Dictionary<int, int>(atomIds.Count);
            for (int i = 0; i < atomIds.Count; i++) idToIndex[atomIds[i]] = i;

            for (int mask = 0; mask < total; mask++)
            {
                for (int i = 0; i < atomIds.Count; i++)
                    assignment[i] = (mask & (1 << i)) != 0;
                if (EvalPropFast(predicate, assignment, idToIndex))
                    return true;
            }
            return false;
        }

        // Limit beyond which brute-force enumeration is bypassed. 2^20 ≈ 10^6
        // assignments is cheap; beyond that we fall back to "conservative
        // true" for unsatisfiability decisions.
        private const int MaxAtomsForBruteForce = 20;

        private static void CollectAtomIds(IStatePredicate p, List<int> ids, HashSet<int> seen)
        {
            switch (p)
            {
                case StatePredTrue _:
                case StatePredFalse _:
                    return;
                case StatePredAtom a:
                    if (seen.Add(a.Prop.Id)) ids.Add(a.Prop.Id);
                    return;
                case StatePredNot n:
                    CollectAtomIds(n.Inner, ids, seen);
                    return;
                case StatePredAnd andN:
                    CollectAtomIds(andN.Left, ids, seen);
                    CollectAtomIds(andN.Right, ids, seen);
                    return;
                case StatePredOr orN:
                    CollectAtomIds(orN.Left, ids, seen);
                    CollectAtomIds(orN.Right, ids, seen);
                    return;
                default:
                    // Foreign IStatePredicate implementations have opaque
                    // semantics; the brute-force decision cannot inspect
                    // them, so we treat the whole sub-tree as a fresh
                    // unconstrained atom by skipping it. The outer
                    // IsSatisfiable falls back to "true" via the assignment
                    // loop because EvalProp will treat unknown predicates as
                    // true (see EvalPropFast).
                    return;
            }
        }

        private static bool EvalPropFast(
            IStatePredicate p, bool[] assignment, Dictionary<int, int> idToIndex)
        {
            switch (p)
            {
                case StatePredTrue _: return true;
                case StatePredFalse _: return false;
                case StatePredAtom a:
                    return idToIndex.TryGetValue(a.Prop.Id, out var i) && assignment[i];
                case StatePredNot n:
                    return !EvalPropFast(n.Inner, assignment, idToIndex);
                case StatePredAnd andN:
                    return EvalPropFast(andN.Left, assignment, idToIndex)
                        && EvalPropFast(andN.Right, assignment, idToIndex);
                case StatePredOr orN:
                    return EvalPropFast(orN.Left, assignment, idToIndex)
                        || EvalPropFast(orN.Right, assignment, idToIndex);
                default:
                    // Unknown predicate type: optimistically assume it can be
                    // true under some assignment (conservative-for-SAT).
                    return true;
            }
        }

        private static bool EvalProp(IStatePredicate p, bool[] assignment)
        {
            switch (p)
            {
                case StatePredTrue _: return true;
                case StatePredFalse _: return false;
                case StatePredNot n: return !EvalProp(n.Inner, assignment);
                case StatePredAnd andN: return EvalProp(andN.Left, assignment) && EvalProp(andN.Right, assignment);
                case StatePredOr orN: return EvalProp(orN.Left, assignment) || EvalProp(orN.Right, assignment);
                default: return true;
            }
        }

        public bool Models(State element, IStatePredicate predicate)
            => predicate.Eval(element);
    }

    /// <summary>
    /// Structural predicate over states. Supports proper equality
    /// for use in the condition registry.
    /// </summary>
    public interface IStatePredicate : IEquatable<IStatePredicate>
    {
        bool Eval(IState state);
    }

    public sealed class StatePredTrue : IStatePredicate
    {
        public static readonly StatePredTrue Instance = new StatePredTrue();
        public bool Eval(IState state) => true;
        public bool Equals(IStatePredicate other) => other is StatePredTrue;
        public override bool Equals(object obj) => obj is StatePredTrue;
        public override int GetHashCode() => 1;
        public override string ToString() => "⊤";
    }

    public sealed class StatePredFalse : IStatePredicate
    {
        public static readonly StatePredFalse Instance = new StatePredFalse();
        public bool Eval(IState state) => false;
        public bool Equals(IStatePredicate other) => other is StatePredFalse;
        public override bool Equals(object obj) => obj is StatePredFalse;
        public override int GetHashCode() => 0;
        public override string ToString() => "⊥";
    }

    public sealed class StatePredAtom : IStatePredicate
    {
        public StateProp Prop { get; }
        public StatePredAtom(StateProp prop) { Prop = prop; }
        public bool Eval(IState state) => Prop.Evaluate((State)state);
        public bool Equals(IStatePredicate other)
            => other is StatePredAtom a && Prop.Id == a.Prop.Id;
        public override bool Equals(object obj) => obj is IStatePredicate p && Equals(p);
        public override int GetHashCode() => Prop.Id * 397 + 2;
        public override string ToString() => Prop.Name;
    }

    public sealed class StatePredNot : IStatePredicate
    {
        public IStatePredicate Inner { get; }
        public StatePredNot(IStatePredicate inner) { Inner = inner; }
        public bool Eval(IState state) => !Inner.Eval(state);
        public bool Equals(IStatePredicate other)
            => other is StatePredNot n && Inner.Equals(n.Inner);
        public override bool Equals(object obj) => obj is IStatePredicate p && Equals(p);
        public override int GetHashCode() => Inner.GetHashCode() * 31 + 3;
        public override string ToString() => $"¬({Inner})";
    }

    public sealed class StatePredAnd : IStatePredicate
    {
        public IStatePredicate Left { get; }
        public IStatePredicate Right { get; }
        public StatePredAnd(IStatePredicate left, IStatePredicate right)
        { Left = left; Right = right; }
        public bool Eval(IState state) => Left.Eval(state) && Right.Eval(state);
        public bool Equals(IStatePredicate other)
            => other is StatePredAnd a && Left.Equals(a.Left) && Right.Equals(a.Right);
        public override bool Equals(object obj) => obj is IStatePredicate p && Equals(p);
        public override int GetHashCode()
        {
            unchecked { return Left.GetHashCode() * 31 + Right.GetHashCode() + 4; }
        }
        public override string ToString() => $"({Left} ∧ {Right})";
    }

    public sealed class StatePredOr : IStatePredicate
    {
        public IStatePredicate Left { get; }
        public IStatePredicate Right { get; }
        public StatePredOr(IStatePredicate left, IStatePredicate right)
        { Left = left; Right = right; }
        public bool Eval(IState state) => Left.Eval(state) || Right.Eval(state);
        public bool Equals(IStatePredicate other)
            => other is StatePredOr o && Left.Equals(o.Left) && Right.Equals(o.Right);
        public override bool Equals(object obj) => obj is IStatePredicate p && Equals(p);
        public override int GetHashCode()
        {
            unchecked { return Left.GetHashCode() * 31 + Right.GetHashCode() + 5; }
        }
        public override string ToString() => $"({Left} ∨ {Right})";
    }
}
