// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Accordant.ModelChecking.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Symbolic;
using Microsoft.Accordant.ModelChecking.Testing;
using NUnit.Framework;

/// <summary>
/// Algebra-level tests for the <see cref="SafeRegex"/> stutter lift.
///
/// <para>Each case pairs a <see cref="SafeRegex"/> with an independent
/// description of its <em>visible</em> language — a predicate over words of
/// changing steps only. The test then enumerates every word over a mixed
/// alphabet of changing and unchanged letters and asserts that the compiled
/// (lowered) expression accepts a word exactly when the visible reference
/// language accepts its erasure. That is precisely the defining property of
/// <c>h⁻¹</c>, where <c>h</c> deletes unchanged letters.</para>
/// </summary>
[TestFixture]
public class SafeRegexLoweringTests
{
    #region Alphabet

    private sealed class Tok : State
    {
        public int V { get; set; }

        protected override void CloneInternal(Dictionary<object, object> clonedMap)
            => clonedMap[this] = new Tok { V = this.V };

        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths, string path, bool forceRecompute)
            => $"V={this.V}";

        protected override void FreezeComponents(HashSet<object> visited)
        {
        }
    }

    private static Tok S(int v)
    {
        var token = new Tok { V = v };
        token.Freeze();
        return token;
    }

    private static TransitionContext Letter(int from, int to)
        => TransitionContext.Edge(S(from), null, null, S(to));

    // 'a', 'b', 'c' are changing letters (the visible alphabet);
    // 'u' and 'v' are unchanged letters and must stay invisible.
    // Note 'u' has source V == 1, so the state observation "V == 1" holds of
    // it — it must still never be matched by a changing-step pattern.
    private static readonly Dictionary<char, TransitionContext> Alphabet =
        new Dictionary<char, TransitionContext>
        {
            ['a'] = Letter(1, 9),
            ['b'] = Letter(2, 9),
            ['c'] = Letter(3, 4),
            ['u'] = Letter(1, 1),
            ['v'] = Letter(5, 5),
        };

    private const string Visible = "abc";
    private const string All = "abcuv";
    private const int MaxWordLength = 4;

    private static string Erase(string word)
        => new string(word.Where(ch => Visible.IndexOf(ch) >= 0).ToArray());

    private static List<TransitionContext> Word(string word)
        => word.Select(ch => Alphabet[ch]).ToList();

    private static IEnumerable<string> AllWords()
    {
        var frontier = new List<string> { string.Empty };
        for (int length = 0; length <= MaxWordLength; length++)
        {
            foreach (var word in frontier) yield return word;
            frontier = frontier
                .SelectMany(prefix => All.Select(ch => prefix + ch))
                .ToList();
        }
    }

    #endregion

    #region Concrete-word matcher over the compiled expression

    private static bool Accepts(
        Ere<IStatePredicate> expression, List<TransitionContext> word, int lo, int hi)
    {
        switch (expression)
        {
            case EreEmpty<IStatePredicate> _:
                return false;

            case EreEpsilon<IStatePredicate> _:
                return lo == hi;

            case EreAtom<IStatePredicate> atom:
                return hi - lo == 1 && atom.Predicate.Eval(word[lo]);

            case EreConcat<IStatePredicate> concat:
                for (int split = lo; split <= hi; split++)
                {
                    if (Accepts(concat.Left, word, lo, split)
                        && Accepts(concat.Right, word, split, hi))
                        return true;
                }

                return false;

            case EreUnion<IStatePredicate> union:
                return union.Operands.Any(op => Accepts(op, word, lo, hi));

            case EreIntersect<IStatePredicate> intersect:
                return intersect.Operands.All(op => Accepts(op, word, lo, hi));

            case EreComplement<IStatePredicate> complement:
                return !Accepts(complement.Inner, word, lo, hi);

            case EreStar<IStatePredicate> star:
                if (lo == hi) return true;
                for (int split = lo + 1; split <= hi; split++)
                {
                    if (Accepts(star.Inner, word, lo, split)
                        && Accepts(star, word, split, hi))
                        return true;
                }

                return false;

            // L(R : S) = { v | ∃ i < |v| : v[..i] ∈ L(R) ∧ v[i..] ∈ L(S) },
            // where v[..i] includes position i. The two operands therefore
            // share one physical letter.
            case EreFusion<IStatePredicate> fusion:
                for (int shared = lo; shared < hi; shared++)
                {
                    if (Accepts(fusion.Left, word, lo, shared + 1)
                        && Accepts(fusion.Right, word, shared, hi))
                        return true;
                }

                return false;

            default:
                throw new NotSupportedException(
                    $"Unexpected node in a lowered SafeRegex: {expression.GetType().Name}");
        }
    }

    private static bool Accepts(SafeRegex pattern, string word)
    {
        var letters = Word(word);
        return Accepts(SafeRegexDiagnostics.Compile(pattern), letters, 0, letters.Count);
    }

    private static bool Accepts(Ere<IStatePredicate> expression, string word)
    {
        var letters = Word(word);
        return Accepts(expression, letters, 0, letters.Count);
    }

    #endregion

    #region Cases

    private static IEnumerable<(string Name, SafeRegex Pattern, Func<string, bool> Language)>
        Cases()
    {
        var f = Formula.For<Tok>();
        var atOne = f.Observe(state => state.V == 1, "V==1");
        var atTwo = f.Observe(state => state.V == 2, "V==2");
        var atThree = f.Observe(state => state.V == 3, "V==3");
        var nonDecreasing = f.ObserveTransition(
            (state, next) => next.V >= state.V, "NonDecreasing");
        var toNine = f.ObserveTransition((state, next) => next.V == 9, "ToNine");

        var a = f.ChangingStep(atOne);
        var b = f.ChangingStep(atTwo);
        var c = f.ChangingStep(atThree);
        var any = f.AnyChangingStep;
        var eps = f.NoChangingSteps;
        var never = f.NeverMatches;

        bool AllOf(string w, params char[] allowed) => w.All(ch => allowed.Contains(ch));

        yield return ("step(a)", a, w => w == "a");
        yield return ("anyStep", any, w => w.Length == 1);
        yield return ("noChangingSteps", eps, w => w.Length == 0);
        yield return ("neverMatches", never, w => false);

        yield return ("a·b", a.Then(b), w => w == "ab");
        yield return ("a|b", a | b, w => w == "a" || w == "b");
        yield return ("a*", a.Star(), w => AllOf(w, 'a'));
        yield return ("a+", a.Plus(), w => w.Length >= 1 && AllOf(w, 'a'));
        yield return ("a?", a.Optional(), w => w.Length == 0 || w == "a");

        yield return ("~a", !a, w => w != "a");
        yield return ("~ε", !eps, w => w.Length > 0);
        yield return ("~∅", !never, w => true);
        yield return ("~(a*)", !a.Star(), w => !AllOf(w, 'a'));
        yield return ("~(a·b)", !a.Then(b), w => w != "ab");
        yield return ("~(any*)", !any.Star(), w => false);

        yield return ("any*", any.Star(), w => true);
        yield return ("(a|b)*·c", (a | b).Star().Then(c),
            w => w.Length >= 1 && w[w.Length - 1] == 'c'
                 && AllOf(w.Substring(0, w.Length - 1), 'a', 'b'));
        yield return ("(a·b)*", a.Then(b).Star(),
            w => w.Length % 2 == 0
                 && Enumerable.Range(0, w.Length / 2)
                     .All(i => w[2 * i] == 'a' && w[2 * i + 1] == 'b'));
        yield return ("(a*)*", a.Star().Star(), w => AllOf(w, 'a'));
        yield return ("a*·b·a*", a.Star().Then(b).Then(a.Star()),
            w => AllOf(w, 'a', 'b') && w.Count(ch => ch == 'b') == 1);

        yield return ("(any·any)* ∩ a*", (any.Then(any)).Star() & a.Star(),
            w => AllOf(w, 'a') && w.Length % 2 == 0);
        yield return ("ε ∩ a*", eps & a.Star(), w => w.Length == 0);
        yield return ("ε | a", eps | a, w => w.Length == 0 || w == "a");
        yield return ("any+ ∩ ~(a·any*)", any.Plus() & !a.Then(any.Star()),
            w => w.Length >= 1 && w[0] != 'a');

        yield return ("step(nonDecreasing)", f.ChangingStep(nonDecreasing),
            w => w.Length == 1);
        yield return ("step(toNine)", f.ChangingStep(toNine),
            w => w.Length == 1 && (w[0] == 'a' || w[0] == 'b'));
        yield return ("step(a|b observation)", f.ChangingStep(atOne | atTwo),
            w => w.Length == 1 && (w[0] == 'a' || w[0] == 'b'));
        yield return ("step(!a observation)", f.ChangingStep(!atOne),
            w => w.Length == 1 && w[0] != 'a');
    }

    #endregion

    [Test]
    public void LoweredLanguage_IsTheInverseImageOfTheVisibleLanguage()
    {
        var words = AllWords().ToList();
        Assert.That(words, Has.Count.GreaterThan(300));

        foreach (var (name, pattern, language) in Cases())
        {
            foreach (var word in words)
            {
                var expected = language(Erase(word));
                var actual = Accepts(pattern, word);
                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    $"{name} on \"{word}\" (erasure \"{Erase(word)}\")");
            }
        }
    }

    [Test]
    public void LoweredEpsilon_AcceptsExactlyTheAllUnchangedWords()
    {
        var f = Formula.For<Tok>();

        Assert.That(Accepts(f.NoChangingSteps, string.Empty), Is.True);
        Assert.That(Accepts(f.NoChangingSteps, "uvu"), Is.True);
        Assert.That(Accepts(f.NoChangingSteps, "ua"), Is.False);
    }

    [Test]
    public void LoweredStar_AcceptsAllUnchangedWords_EvenWhenBodyIsNotNullable()
    {
        var f = Formula.For<Tok>();
        var star = f.ChangingStep(f.Observe(state => state.V == 1)).Star();

        // ε ∈ L(a*) so every all-unchanged word must be accepted, even though
        // the body itself matches no all-unchanged word.
        Assert.That(Accepts(star, "uvuv"), Is.True);
        Assert.That(Accepts(star, "uau"), Is.True);
        Assert.That(Accepts(star, "ubu"), Is.False);
    }

    [Test]
    public void LoweredComplement_IsTakenOverChangingStepWordsOnly()
    {
        var f = Formula.For<Tok>();
        var notEpsilon = !f.NoChangingSteps;

        Assert.That(Accepts(notEpsilon, "uvu"), Is.False);
        Assert.That(Accepts(notEpsilon, "uau"), Is.True);
    }

    [Test]
    public void CanonicalUnchangedPredicate_ClassifiesTheTestAlphabet()
    {
        var unchanged = SafeRegexDiagnostics.UnchangedStepPredicate;

        foreach (var entry in Alphabet)
        {
            var expectedUnchanged = All.IndexOf(entry.Key) >= Visible.Length;
            Assert.That(
                unchanged.Eval(entry.Value),
                Is.EqualTo(expectedUnchanged),
                $"letter '{entry.Key}'");
        }
    }

    /// <summary>
    /// The defining stutter-invariance property, stated directly: two words
    /// with the same erasure are accepted or rejected together. Grouping by
    /// erasure and asserting a uniform verdict is exactly closure under
    /// insertion <em>and</em> deletion of unchanged steps.
    /// </summary>
    [Test]
    public void LoweredLanguages_AreClosedUnderInsertingAndDeletingUnchangedSteps()
    {
        var groups = AllWords()
            .GroupBy(Erase)
            .Where(group => group.Count() > 1)
            .ToList();
        Assert.That(groups, Has.Count.GreaterThan(30));

        foreach (var (name, pattern, _) in Cases())
        {
            var compiled = SafeRegexDiagnostics.Compile(pattern);
            foreach (var group in groups)
            {
                var representative = group.First();
                var expected = Accepts(compiled, representative);
                foreach (var word in group)
                {
                    Assert.That(
                        Accepts(compiled, word),
                        Is.EqualTo(expected),
                        $"{name}: \"{word}\" and \"{representative}\" both erase to " +
                        $"\"{group.Key}\" but are classified differently");
                }
            }
        }
    }

    /// <summary>
    /// Negative control at the regular-language level: an ERE built directly
    /// over physical transition letters — the shape
    /// <see cref="RegexPattern"/> produces — is <em>not</em> stutter
    /// invariant. It distinguishes "ab" from "aub", whose erasures are equal.
    /// This is the concrete reason <see cref="SafeRegex"/> exists.
    /// </summary>
    [Test]
    public void RawTransitionLetterConcatenation_IsNotStutterInvariant()
    {
        var atOne = new StatePredAtom(new StateProp("V==1", state => ((Tok)state).V == 1));
        var atTwo = new StatePredAtom(new StateProp("V==2", state => ((Tok)state).V == 2));
        var raw = Ere<IStatePredicate>.Concat(
            Ere<IStatePredicate>.Atom(atOne),
            Ere<IStatePredicate>.Atom(atTwo));

        Assert.That(Accepts(raw, "ab"), Is.True);
        Assert.That(Accepts(raw, "aub"), Is.False, "one inserted unchanged letter breaks it");

        // The stutter-safe counterpart accepts both.
        var f = Formula.For<Tok>();
        var safe = f.ChangingStep(f.Observe(state => state.V == 1, "V==1"))
            .Then(f.ChangingStep(f.Observe(state => state.V == 2, "V==2")));
        Assert.That(Accepts(safe, "ab"), Is.True);
        Assert.That(Accepts(safe, "aub"), Is.True);
    }

    /// <summary>
    /// Negative control for fusion, the reason it is deliberately absent from
    /// <see cref="SafeRegex"/>. Fusion makes the two operands share one
    /// <em>physical</em> letter. Applied to lowered (already stutter-safe)
    /// operands it still breaks invariance, because an inserted unchanged
    /// letter can serve as the shared boundary: <c>⌈a⌉ : ⌈b⌉</c> rejects
    /// "ab" but accepts "aub", although both erase to "ab".
    /// </summary>
    [Test]
    public void Fusion_OfLoweredPatterns_IsNotStutterInvariant()
    {
        var f = Formula.For<Tok>();
        var a = f.ChangingStep(f.Observe(state => state.V == 1, "V==1"));
        var b = f.ChangingStep(f.Observe(state => state.V == 2, "V==2"));

        var fused = Ere<IStatePredicate>.Fusion(
            SafeRegexDiagnostics.Compile(a),
            SafeRegexDiagnostics.Compile(b));

        Assert.That(Accepts(fused, "ab"), Is.False, "no letter can be shared");
        Assert.That(
            Accepts(fused, "aub"),
            Is.True,
            "the inserted unchanged letter 'u' can be shared by both operands");

        // Concatenation of the same lowered operands — the operator SafeRegex
        // does expose — treats the two words alike.
        var concatenated = Ere<IStatePredicate>.Concat(
            SafeRegexDiagnostics.Compile(a),
            SafeRegexDiagnostics.Compile(b));
        Assert.That(Accepts(concatenated, "ab"), Is.True);
        Assert.That(Accepts(concatenated, "aub"), Is.True);
    }

    /// <summary>
    /// The overlap performed by the stutter-sensitive <c>OvlPrefix</c> and
    /// <c>Match</c> operators, at the language level: they hand the last
    /// letter of the match to the suffix formula. An inserted unchanged step
    /// moves that letter, so the position handed over is not stable. Here
    /// <c>⌈a⌉</c> ends on the changing letter in "ab" but can end on the
    /// unchanged letter in "aub".
    /// </summary>
    [Test]
    public void OverlapBoundary_OfALoweredPattern_IsNotStable()
    {
        var f = Formula.For<Tok>();
        var compiled = SafeRegexDiagnostics.Compile(
            f.ChangingStep(f.Observe(state => state.V == 1, "V==1")));

        // The overlap position is the index of the last matched letter.
        List<int> OverlapPositions(string word)
        {
            var letters = Word(word);
            return Enumerable.Range(0, letters.Count)
                .Where(index => Accepts(compiled, letters, 0, index + 1))
                .ToList();
        }

        Assert.That(OverlapPositions("ab"), Is.EquivalentTo(new[] { 0 }));
        Assert.That(
            OverlapPositions("aub"),
            Is.EquivalentTo(new[] { 0, 1 }),
            "with a stutter inserted the pattern may also end one letter later");
    }

    [Test]
    public void ToString_RendersVisibleStepsWithoutUnchangedPadding()
    {
        var f = Formula.For<Tok>();
        var a = f.ChangingStep(f.Observe(state => state.V == 1, "P"));
        var b = f.ChangingStep(f.Observe(state => state.V == 2, "Q"));

        Assert.That(a.ToString(), Is.EqualTo("⟨P⟩"));
        Assert.That(a.Then(b).ToString(), Is.EqualTo("(⟨P⟩ · ⟨Q⟩)"));
        Assert.That((a | b).ToString(), Is.EqualTo("(⟨P⟩ + ⟨Q⟩)"));
        Assert.That((a & b).ToString(), Is.EqualTo("(⟨P⟩ ∩ ⟨Q⟩)"));
        Assert.That((!a).ToString(), Is.EqualTo("~⟨P⟩"));
        Assert.That(a.Star().ToString(), Is.EqualTo("⟨P⟩*"));
        Assert.That(a.Plus().ToString(), Is.EqualTo("⟨P⟩+"));
        Assert.That(a.Optional().ToString(), Is.EqualTo("⟨P⟩?"));
        Assert.That(f.NoChangingSteps.ToString(), Is.EqualTo("ε"));
        Assert.That(f.NeverMatches.ToString(), Is.EqualTo("∅"));
        Assert.That(f.AnyChangingStep.ToString(), Is.EqualTo("⟨step⟩"));
    }
}
