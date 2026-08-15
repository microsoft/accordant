namespace Microsoft.Accordant.ModelChecking
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant.ModelChecking.Symbolic;

    internal static class FiniteInvariantCheck
    {
        internal static PropertyCheckingResult FindViolation(
            StateGraphNode root,
            Ltl<IStatePredicate> property,
            int maxDepth)
        {
            return TryBuildInvariant(property, out var invariant)
                ? FindViolation(root, invariant, maxDepth)
                : null;
        }

        internal static PropertyCheckingResult FindViolation(
            StateGraphNode root,
            Rltl<IStatePredicate> property,
            int maxDepth)
        {
            return TryBuildInvariant(property, out var invariant)
                ? FindViolation(root, invariant, maxDepth)
                : null;
        }

        private static PropertyCheckingResult FindViolation(
            StateGraphNode root,
            Func<IState, bool> invariant,
            int maxDepth)
        {
            var rootFingerprint = root.GetNodeFingerprint();
            var parent = new Dictionary<string, (string fingerprint, IStepFunction step)>
            {
                [rootFingerprint] = (null, null)
            };
            var nodes = new Dictionary<string, StateGraphNode>
            {
                [rootFingerprint] = root
            };
            var queue = new Queue<(StateGraphNode node, int depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (!invariant(node.State))
                {
                    return PropertyCheckingResult.Failure(
                        BuildTrace(node.GetNodeFingerprint(), parent, nodes));
                }

                if (maxDepth > 0 && depth >= maxDepth)
                {
                    continue;
                }

                foreach (var edge in node.Edges)
                {
                    var fingerprint = edge.Target.GetNodeFingerprint();
                    if (parent.ContainsKey(fingerprint))
                    {
                        continue;
                    }

                    parent[fingerprint] = (node.GetNodeFingerprint(), edge.StepFunction);
                    nodes[fingerprint] = edge.Target;
                    queue.Enqueue((edge.Target, depth + 1));
                }
            }

            return null;
        }

        private static List<TraceItem> BuildTrace(
            string violation,
            IReadOnlyDictionary<string, (string fingerprint, IStepFunction step)> parent,
            IReadOnlyDictionary<string, StateGraphNode> nodes)
        {
            var reversed = new List<TraceItem>();
            for (var fingerprint = violation; fingerprint != null;)
            {
                var link = parent[fingerprint];
                reversed.Add(new TraceItem(link.step, nodes[fingerprint], isInCycle: false));
                fingerprint = link.fingerprint;
            }

            reversed.Reverse();
            return reversed;
        }

        private static bool TryBuildInvariant(
            Ltl<IStatePredicate> property,
            out Func<IState, bool> invariant)
        {
            if (property is LtlRelease<IStatePredicate> release &&
                release.Left is LtlFalse<IStatePredicate>)
            {
                return TryBuildProposition(release.Right, out invariant);
            }

            invariant = null;
            return false;
        }

        private static bool TryBuildInvariant(
            Rltl<IStatePredicate> property,
            out Func<IState, bool> invariant)
        {
            if (property is RltlRelease<IStatePredicate> release &&
                release.Left is RltlFalse<IStatePredicate>)
            {
                return TryBuildProposition(release.Right, out invariant);
            }

            invariant = null;
            return false;
        }

        private static bool TryBuildProposition(
            Ltl<IStatePredicate> formula,
            out Func<IState, bool> predicate)
        {
            switch (formula)
            {
                case LtlTrue<IStatePredicate> _:
                    predicate = _ => true;
                    return true;
                case LtlFalse<IStatePredicate> _:
                    predicate = _ => false;
                    return true;
                case LtlAtom<IStatePredicate> atom when !atom.Predicate.IsTransitionAware:
                    predicate = atom.Predicate.Eval;
                    return true;
                case LtlAnd<IStatePredicate> and:
                    return TryBuildConjunction(and.Operands, TryBuildProposition, out predicate);
                case LtlOr<IStatePredicate> or:
                    return TryBuildDisjunction(or.Operands, TryBuildProposition, out predicate);
                default:
                    predicate = null;
                    return false;
            }
        }

        private static bool TryBuildProposition(
            Rltl<IStatePredicate> formula,
            out Func<IState, bool> predicate)
        {
            switch (formula)
            {
                case RltlTrue<IStatePredicate> _:
                    predicate = _ => true;
                    return true;
                case RltlFalse<IStatePredicate> _:
                    predicate = _ => false;
                    return true;
                case RltlAtom<IStatePredicate> atom when !atom.Predicate.IsTransitionAware:
                    predicate = atom.Predicate.Eval;
                    return true;
                case RltlAnd<IStatePredicate> and:
                    return TryBuildConjunction(and.Operands, TryBuildProposition, out predicate);
                case RltlOr<IStatePredicate> or:
                    return TryBuildDisjunction(or.Operands, TryBuildProposition, out predicate);
                default:
                    predicate = null;
                    return false;
            }
        }

        private static bool TryBuildConjunction<TFormula>(
            IReadOnlyList<TFormula> operands,
            TryBuildPredicate<TFormula> tryBuild,
            out Func<IState, bool> predicate)
        {
            var predicates = new List<Func<IState, bool>>();
            foreach (var operand in operands)
            {
                if (!tryBuild(operand, out var child))
                {
                    predicate = null;
                    return false;
                }
                predicates.Add(child);
            }

            predicate = state => predicates.All(p => p(state));
            return true;
        }

        private static bool TryBuildDisjunction<TFormula>(
            IReadOnlyList<TFormula> operands,
            TryBuildPredicate<TFormula> tryBuild,
            out Func<IState, bool> predicate)
        {
            var predicates = new List<Func<IState, bool>>();
            foreach (var operand in operands)
            {
                if (!tryBuild(operand, out var child))
                {
                    predicate = null;
                    return false;
                }
                predicates.Add(child);
            }

            predicate = state => predicates.Any(p => p(state));
            return true;
        }

        private delegate bool TryBuildPredicate<TFormula>(
            TFormula formula,
            out Func<IState, bool> predicate);
    }
}
