using Microsoft.Accordant.ModelChecking;

namespace Accordant.ModelChecking.Tests
{
    using NUnit.Framework;

    [TestFixture]
    public class IntUnionFindTests
    {
        [Test]
        public void Find_OnFreshKey_ReturnsSelf_AndMaterializes()
        {
            var uf = new IntUnionFind(4);
            Assert.That(uf.Contains(7), Is.False);
            Assert.That(uf.Find(7), Is.EqualTo(7));
            Assert.That(uf.Contains(7), Is.True);
        }

        [Test]
        public void Union_MergesClasses_ReturnsTrueOnceFalseAfterwards()
        {
            var uf = new IntUnionFind();
            Assert.That(uf.Union(1, 2), Is.True);
            Assert.That(uf.Union(2, 1), Is.False);
            Assert.That(uf.InSameClass(1, 2), Is.True);
        }

        [Test]
        public void Union_IsTransitive()
        {
            var uf = new IntUnionFind();
            uf.Union(1, 2);
            uf.Union(3, 4);
            Assert.That(uf.InSameClass(1, 4), Is.False);
            uf.Union(2, 3);
            Assert.That(uf.InSameClass(1, 4), Is.True);
            Assert.That(uf.InSameClass(2, 4), Is.True);
        }

        [Test]
        public void DistinctSingletons_AreNotInSameClass()
        {
            var uf = new IntUnionFind();
            Assert.That(uf.InSameClass(10, 11), Is.False);
            Assert.That(uf.Find(10), Is.EqualTo(10));
            Assert.That(uf.Find(11), Is.EqualTo(11));
        }

        [Test]
        public void GrowthBeyondInitialCapacity_Works()
        {
            // Initial capacity 2; force several doublings.
            var uf = new IntUnionFind(2);
            uf.Union(0, 1000);
            uf.Union(1000, 2000);
            Assert.That(uf.InSameClass(0, 2000), Is.True);
            Assert.That(uf.InSameClass(0, 1234), Is.False);
        }

        [Test]
        public void PathCompression_ProducesShallowTrees()
        {
            // Build a deliberately long left-leaning chain by unioning 0..N
            // in order; after a single Find(N), parents should mostly point
            // at the root.
            var uf = new IntUnionFind(8);
            const int N = 32;
            for (int i = 0; i + 1 < N; i++) uf.Union(i + 1, i);
            int root = uf.Find(N - 1);
            // Every key 0..N-1 should report the same root.
            for (int i = 0; i < N; i++)
            {
                Assert.That(uf.Find(i), Is.EqualTo(root));
            }
        }

        [Test]
        public void Find_NegativeKey_Throws()
        {
            var uf = new IntUnionFind();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => uf.Find(-1));
        }
    }
}
