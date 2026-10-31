namespace Microsoft.Accordant.ModelChecking.Bdd
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    /// <summary>
    /// BDD-backed Effective Boolean Algebra over
    /// <see cref="IStatePredicate"/>.
    ///
    /// <para>
    /// Structural operations (<see cref="And"/>, <see cref="Or"/>,
    /// <see cref="Not"/>, <see cref="Top"/>, <see cref="Bottom"/>) and
    /// the <see cref="Models"/> relation are delegated to the toy
    /// <see cref="StatePropEba"/> so the rest of the engine keeps seeing
    /// the existing structural <see cref="IStatePredicate"/> trees and
    /// nothing about the predicate IR changes.
    /// </para>
    ///
    /// <para>
    /// What this adapter <em>does</em> add is precise propositional
    /// decisions: <see cref="IsSatisfiable"/>, <see cref="AreEquivalent"/>
    /// and <see cref="Implies"/> all translate the predicate into the
    /// in-box BDD package via <see cref="CharSetSolverBridge"/> and then
    /// rely on BDD canonicalisation:
    /// <list type="bullet">
    ///   <item><c>IsSatisfiable(p)  ≡  bdd(p) ≠ ⊥</c></item>
    ///   <item><c>AreEquivalent(p,q) ≡ bdd(p) == bdd(q)</c></item>
    ///   <item><c>Implies(p,q)      ≡ bdd(p ∧ ¬q) == ⊥</c></item>
    /// </list>
    /// All of these are O(1) on top of the apply cost, with no atom-count
    /// cap. The toy backend's brute-force enumeration caps out at 20
    /// atoms; this adapter doesn't.
    /// </para>
    ///
    /// <para>
    /// Atoms are mapped to BDD variable ordinals on first sight, indexed
    /// by <see cref="StateProp.Id"/>. The mapping is per-instance so
    /// equivalent atoms across calls on the same adapter hit the same
    /// BDD variable.
    /// </para>
    ///
    /// <para>
    /// <see cref="IEffectiveBooleanAlgebraEx{T,E}.TryGetModel"/> is
    /// deliberately not implemented (the adapter would need a way to
    /// fabricate a <see cref="State"/> from an assignment of opaque atom
    /// callbacks, which it does not have). <see cref="EbaExtensions.TryGetModel"/>
    /// callers therefore still get a <c>false</c> via the fallback.
    /// </para>
    /// </summary>
    public sealed class BddStatePropEba
        : IEffectiveBooleanAlgebra<IStatePredicate, State>,
          IPredicateAlgebraEx<IStatePredicate>
    {
        /// <summary>Shared instance — atom ordinal cache is intentionally
        /// per-process so independent callers re-use the same BDD nodes.</summary>
        public static readonly BddStatePropEba Instance = new BddStatePropEba();

        private readonly CharSetSolverBridge _bridge = CharSetSolverBridge.Instance;
        private readonly StatePropEba _structural = StatePropEba.Instance;

        // StateProp.Id -> BDD ordinal. Allocate-on-first-sight; ordinals
        // monotonically increase. Variable order matters for BDD size; a
        // first-seen heuristic is the simplest reasonable default.
        private readonly Dictionary<int, int> _propToOrdinal = new Dictionary<int, int>();
        // Reverse map, useful for diagnostics / future model-lift.
        private readonly Dictionary<int, StateProp> _ordinalToProp = new Dictionary<int, StateProp>();
        private int _nextOrdinal;
        private readonly object _mapGate = new object();

        // --- Structural ops (delegate) -------------------------------------

        public IStatePredicate Top    => _structural.Top;
        public IStatePredicate Bottom => _structural.Bottom;

        public IStatePredicate And(IStatePredicate a, IStatePredicate b)
            => _structural.And(a, b);

        public IStatePredicate Or(IStatePredicate a, IStatePredicate b)
            => _structural.Or(a, b);

        public IStatePredicate Not(IStatePredicate a)
            => _structural.Not(a);

        public bool Models(State element, IStatePredicate predicate)
            => _structural.Models(element, predicate);

        // --- Decisions (precise, BDD-backed) -------------------------------

        public bool IsSatisfiable(IStatePredicate predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (predicate is StatePredFalse) return false;
            if (predicate is StatePredTrue)  return true;
            if (TryEncode(predicate, out var bdd))
                return !_bridge.IsFalse(bdd);
            // Foreign IStatePredicate subtree -> conservative-true, matching
            // the toy and the EBA contract.
            return true;
        }

        public bool AreEquivalent(IStatePredicate a, IStatePredicate b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (ReferenceEquals(a, b)) return true;
            if (!TryEncode(a, out var ba)) return false;
            if (!TryEncode(b, out var bb)) return false;
            return _bridge.AreSame(ba, bb);
        }

        public bool Implies(IStatePredicate a, IStatePredicate b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (!TryEncode(a, out var ba)) return false;
            if (!TryEncode(b, out var bb)) return false;
            // a ⇒ b   iff   a ∧ ¬b  ≡  ⊥
            var notB = _bridge.Not(bb);
            var conj = _bridge.And(ba, notB);
            return _bridge.IsFalse(conj);
        }

        // --- Encoding ------------------------------------------------------

        private int OrdinalFor(StateProp prop)
        {
            lock (_mapGate)
            {
                if (_propToOrdinal.TryGetValue(prop.Id, out var ord)) return ord;
                ord = _nextOrdinal++;
                _propToOrdinal[prop.Id] = ord;
                _ordinalToProp[ord] = prop;
                return ord;
            }
        }

        /// <summary>
        /// Translate a propositional <see cref="IStatePredicate"/> tree to
        /// a BDD. Returns <c>false</c> if the tree contains a foreign
        /// subclass we cannot interpret. In that case the caller falls
        /// back to a conservative answer rather than risking unsoundness.
        /// </summary>
        private bool TryEncode(IStatePredicate p, out object bdd)
        {
            switch (p)
            {
                case StatePredTrue _:
                    bdd = _bridge.Top;
                    return true;
                case StatePredFalse _:
                    bdd = _bridge.Bottom;
                    return true;
                case StatePredAtom atom:
                    bdd = _bridge.MkVar(OrdinalFor(atom.Prop));
                    return true;
                case StatePredNot neg:
                    if (!TryEncode(neg.Inner, out var inner)) { bdd = null; return false; }
                    bdd = _bridge.Not(inner);
                    return true;
                case StatePredAnd conj:
                    if (!TryEncode(conj.Left,  out var la) ||
                        !TryEncode(conj.Right, out var ra))
                    { bdd = null; return false; }
                    bdd = _bridge.And(la, ra);
                    return true;
                case StatePredOr disj:
                    if (!TryEncode(disj.Left,  out var ld) ||
                        !TryEncode(disj.Right, out var rd))
                    { bdd = null; return false; }
                    bdd = _bridge.Or(ld, rd);
                    return true;
                default:
                    bdd = null;
                    return false;
            }
        }
    }
}
