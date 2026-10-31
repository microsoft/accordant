using Microsoft.Accordant;

namespace Microsoft.Accordant.ModelChecking.Testing
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant.ModelChecking.Ltl;

    /// <summary>
    /// Bounded-depth random LTL formula generator over a fixed atomic-
    /// proposition vocabulary. Used to drive
    /// <see cref="LtlMultiBackendCrossCheck"/> for differential bug
    /// hunting: a disagreement on any single generated formula
    /// localizes a real soundness gap in one of the backends.
    ///
    /// <para>
    /// The grammar covers the full LTL fragment shared by the four
    /// backends:
    /// </para>
    /// <list type="bullet">
    /// <item><c>True | False | p_i | ¬p_i</c> (leaves)</item>
    /// <item><c>¬φ | φ ∧ ψ | φ ∨ ψ</c></item>
    /// <item><c>Xφ | φ U ψ | φ R ψ</c></item>
    /// </list>
    ///
    /// <para>
    /// Tree shape is controlled by <c>maxDepth</c>; at depth 0 only
    /// leaves are produced. The distribution is mildly biased toward
    /// temporal operators so that random formulas stress the
    /// derivative / NBW construction rather than degenerating into
    /// pure-boolean tautologies.
    /// </para>
    /// </summary>
    public sealed class RandomLtlGenerator
    {
        private readonly Random _rng;
        private readonly IReadOnlyList<(Func<IState, bool> pred, string name)> _atoms;

        public RandomLtlGenerator(int seed, IReadOnlyList<(Func<IState, bool> pred, string name)> atoms)
        {
            if (atoms == null || atoms.Count == 0)
                throw new ArgumentException("At least one atomic proposition required.", nameof(atoms));
            _rng = new Random(seed);
            _atoms = atoms;
        }

        /// <summary>
        /// Generates a single random LTL formula whose syntax tree
        /// has depth at most <paramref name="maxDepth"/>.
        /// </summary>
        public LtlFormula Generate(int maxDepth)
        {
            if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            return Build(maxDepth);
        }

        private LtlFormula Build(int depth)
        {
            // At depth 0 we must emit a leaf.
            if (depth == 0) return RandomLeaf();

            // Otherwise pick a node weighted toward temporal operators.
            // Weights: leaf=2, ¬=1, ∧=2, ∨=2, X=2, U=3, R=3 (sum = 15).
            var roll = _rng.Next(15);
            if (roll < 2)       return RandomLeaf();
            if (roll < 3)       return LtlFormula.Not(Build(depth - 1));
            if (roll < 5)       return LtlFormula.And(Build(depth - 1), Build(depth - 1));
            if (roll < 7)       return LtlFormula.Or(Build(depth - 1), Build(depth - 1));
            if (roll < 9)       return LtlFormula.Next(Build(depth - 1));
            if (roll < 12)      return LtlFormula.Until(Build(depth - 1), Build(depth - 1));
            return                     LtlFormula.Release(Build(depth - 1), Build(depth - 1));
        }

        private LtlFormula RandomLeaf()
        {
            var roll = _rng.Next(_atoms.Count + 2);
            if (roll == _atoms.Count)     return LtlFormula.True;
            if (roll == _atoms.Count + 1) return LtlFormula.False;
            var (pred, name) = _atoms[roll];
            // Randomly negate atoms half the time to exercise atom-level NNF.
            if (_rng.Next(2) == 0)
                return LtlFormula.Prop(pred, name);
            return LtlFormula.Not(LtlFormula.Prop(pred, name));
        }
    }
}
