namespace Accordant.ModelChecking.Tests
{
    using System.Linq;
    using Microsoft.Accordant.ModelChecking;
    using NUnit.Framework;

    [TestFixture]
    public class ConsListTests
    {
        [Test]
        public void Empty_IsEmpty()
        {
            Assert.That(ConsList<int>.Empty.IsEmpty, Is.True);
            Assert.That(ConsList<int>.Empty.Count, Is.EqualTo(0));
            Assert.That(ConsList<int>.Empty.ToArray(), Is.Empty);
        }

        [Test]
        public void Cons_HeadAndTail()
        {
            var l = ConsList<int>.Empty.Push(1).Push(2).Push(3);
            Assert.That(l.Head, Is.EqualTo(3));
            Assert.That(l.Count, Is.EqualTo(3));
            Assert.That(l.ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
        }

        [Test]
        public void StructuralSharing_TailSurvives()
        {
            var a = ConsList<int>.Empty.Push(1).Push(2);
            var b = a.Push(3);
            var c = a.Push(99);
            Assert.That(a.ToArray(), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(b.ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(c.ToArray(), Is.EqualTo(new[] { 99, 2, 1 }));
            // Same tail node reused.
            Assert.That(ReferenceEquals(b.Tail, a), Is.True);
            Assert.That(ReferenceEquals(c.Tail, a), Is.True);
        }

        [Test]
        public void Reverse_RoundTrip()
        {
            var l = ConsList<string>.Empty.Push("a").Push("b").Push("c");
            Assert.That(l.Reverse().ToArray(), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(l.Reverse().Reverse().ToArray(),
                Is.EqualTo(new[] { "c", "b", "a" }));
        }

        [Test]
        public void FromEnumerableReversed_Order()
        {
            var l = ConsList<int>.FromEnumerableReversed(new[] { 1, 2, 3 });
            // After folding with prepend, head is last input.
            Assert.That(l.ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
        }

        [Test]
        public void EmptySingleton_IsShared()
        {
            Assert.That(ReferenceEquals(ConsList<int>.Empty, ConsList<int>.Empty), Is.True);
        }
    }
}
