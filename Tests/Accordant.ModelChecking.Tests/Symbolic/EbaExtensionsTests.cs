using Microsoft.Accordant.ModelChecking;

namespace Accordant.ModelChecking.Tests.Symbolic
{
    using Microsoft.Accordant.ModelChecking.Symbolic;
    using NUnit.Framework;

    /// <summary>
    /// Tests for <see cref="EbaExtensions"/>: capability-probing
    /// extension methods over <see cref="IPredicateAlgebra{T}"/> /
    /// <see cref="IEffectiveBooleanAlgebra{TP,TE}"/>. Covers both the
    /// <c>Ex</c>-interface dispatch path and the
    /// <c>IsSatisfiable</c>-based fallback.
    /// </summary>
    [TestFixture]
    public class EbaExtensionsTests
    {
        [Test]
        public void AreEquivalent_DispatchesToExWhenAvailable()
        {
            var eba = new IntEba(4);
            var a = new IntPredicate("a", 0, 1);
            var b = new IntPredicate("b", 1, 0);
            Assert.IsTrue(eba.AreEquivalent(a, b));
            Assert.IsFalse(eba.AreEquivalent(a, new IntPredicate("c", 0)));
        }

        [Test]
        public void Implies_DispatchesToExWhenAvailable()
        {
            var eba = new IntEba(4);
            var a = new IntPredicate("a", 0, 1);
            var b = new IntPredicate("b", 0, 1, 2);
            Assert.IsTrue(eba.Implies(a, b));
            Assert.IsFalse(eba.Implies(b, a));
        }

        [Test]
        public void TryGetModel_DispatchesToExWhenAvailable()
        {
            var eba = new IntEba(4);
            var sat = new IntPredicate("p", 2, 3);
            Assert.IsTrue(eba.TryGetModel(sat, out var m));
            Assert.IsTrue(eba.Models(m, sat));

            Assert.IsFalse(eba.TryGetModel(eba.Bottom, out _));
        }

        [Test]
        public void AreEquivalent_FallbackUsesIsSatisfiable()
        {
            var wrap = new HidingEba<IntPredicate, int>(new IntEba(4));
            var a = new IntPredicate("a", 1, 2);
            var b = new IntPredicate("b", 2, 1);
            Assert.IsTrue(wrap.AreEquivalent(a, b));
            Assert.IsFalse(wrap.AreEquivalent(a, new IntPredicate("c", 1)));
        }

        [Test]
        public void Implies_FallbackUsesIsSatisfiable()
        {
            var wrap = new HidingEba<IntPredicate, int>(new IntEba(4));
            var a = new IntPredicate("a", 0);
            var b = new IntPredicate("b", 0, 1);
            Assert.IsTrue(wrap.Implies(a, b));
            Assert.IsFalse(wrap.Implies(b, a));
        }

        [Test]
        public void TryGetModel_FallbackReturnsFalse()
        {
            var wrap = new HidingEba<IntPredicate, int>(new IntEba(4));
            var sat = new IntPredicate("p", 2);
            Assert.IsFalse(wrap.TryGetModel(sat, out var m));
            Assert.AreEqual(default(int), m);
        }

        /// <summary>
        /// Wraps an EBA but does NOT implement the Ex interfaces, forcing
        /// the extension methods onto their fallback paths.
        /// </summary>
        private sealed class HidingEba<TP, TE> : IEffectiveBooleanAlgebra<TP, TE>
        {
            private readonly IEffectiveBooleanAlgebra<TP, TE> _inner;
            public HidingEba(IEffectiveBooleanAlgebra<TP, TE> inner) { _inner = inner; }
            public TP Top => _inner.Top;
            public TP Bottom => _inner.Bottom;
            public TP And(TP a, TP b) => _inner.And(a, b);
            public TP Or(TP a, TP b) => _inner.Or(a, b);
            public TP Not(TP a) => _inner.Not(a);
            public bool IsSatisfiable(TP p) => _inner.IsSatisfiable(p);
            public bool Models(TE element, TP p) => _inner.Models(element, p);
        }
    }

    /// <summary>
    /// Tests for the EBA-aware <see cref="EreWitness.Materialise{TP,TE}(ConsList{TP},
    /// IEffectiveBooleanAlgebra{TP,TE}, System.Func{TP,TE})"/> overload.
    /// </summary>
    [TestFixture]
    public class EreWitnessMaterialiseExTests
    {
        [Test]
        public void Materialise_UsesEbaTryGetModel()
        {
            var eba = new IntEba(4);
            var p0 = new IntPredicate("p0", 0, 1);
            var p1 = new IntPredicate("p1", 2);
            // Push: head = last pushed; ToForward reverses, so forward = [p0, p1].
            var reversed = ConsList<IntPredicate>.Empty.Push(p0).Push(p1);
            var concrete = EreWitness.Materialise<IntPredicate, int>(reversed, eba);
            Assert.AreEqual(2, concrete.Count);
            Assert.IsTrue(eba.Models(concrete[0], p0));
            Assert.AreEqual(2, concrete[1]);
        }

        [Test]
        public void Materialise_FallsBackToChooseModelWhenEbaCannotModel()
        {
            var wrap = new ForwardingEba(new IntEba(4));
            var p = new IntPredicate("p", 1, 2);
            var reversed = ConsList<IntPredicate>.Empty.Push(p);
            var concrete = EreWitness.Materialise<IntPredicate, int>(reversed, wrap,
                chooseModel: pred => 1);
            Assert.AreEqual(1, concrete.Count);
            Assert.AreEqual(1, concrete[0]);
        }

        private sealed class ForwardingEba : IEffectiveBooleanAlgebra<IntPredicate, int>
        {
            private readonly IntEba _inner;
            public ForwardingEba(IntEba inner) { _inner = inner; }
            public IntPredicate Top => _inner.Top;
            public IntPredicate Bottom => _inner.Bottom;
            public IntPredicate And(IntPredicate a, IntPredicate b) => _inner.And(a, b);
            public IntPredicate Or(IntPredicate a, IntPredicate b) => _inner.Or(a, b);
            public IntPredicate Not(IntPredicate a) => _inner.Not(a);
            public bool IsSatisfiable(IntPredicate p) => _inner.IsSatisfiable(p);
            public bool Models(int e, IntPredicate p) => _inner.Models(e, p);
        }
    }
}
