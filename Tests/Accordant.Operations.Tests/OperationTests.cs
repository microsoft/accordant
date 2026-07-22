// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Test helper to create ResponseValidators that check for equality.
/// Replaces Descriptor.FromValue functionality for tests.
/// </summary>
internal static class Descriptor
{
    public static ResponseValidator FromValue<T>(T expectedValue)
    {
        return ResponseValidator.FromPredicate<T>(response => Equals(response, expectedValue));
    }
}

/// <summary>
/// These tests mirror BehaviorTests exactly, but use Operation&lt;&gt; instead of Behavior&lt;&gt;.
/// This ensures we have no regression when we eliminate Behavior&lt;&gt;.
/// </summary>
[TestFixture]
public class OperationTests
{
    [Test]
    public void SimpleOperationValidationTests()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var stateProfile = new StateProfile(new CounterState(1));

        (var success, var message, stateProfile) = spec.Allows(
            spec.Mirror,
            1,
            1,
            stateProfile);

        Assert.IsTrue(success, message);
        Assert.IsTrue(stateProfile != null);

        (success, message, stateProfile) = spec.Allows(
            spec.Mirror,
            1,
            2,
            stateProfile);

        Assert.IsFalse(success);
        Assert.IsTrue(message != null && message.Length > 0);
        Assert.IsTrue(stateProfile == null);

        stateProfile = new StateProfile(new State[]
        {
            new CounterState(1),
            new CounterState(2)
        });

        (success, var secondMessage, stateProfile) = spec.Allows(
            spec.Mirror,
            1,
            2,
            stateProfile);

        Assert.IsFalse(success);
        Assert.IsTrue(secondMessage != null && secondMessage.Length > message.Length);
        Assert.IsTrue(stateProfile == null);
    }

    [Test]
    public void SimpleOperationConcurrentValidationTests()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var stateProfile = new StateProfile(new CounterState(1));

        // AllowsConcurrent validates concurrent operation responses
        (var success, var message, stateProfile) = spec.AllowsConcurrent(
            stateProfile,
            new (IOperation operation, object request, object response)[]
            {
                (spec.Mirror, 1, 1),
                (spec.Mirror, 2, 2),
            });

        Assert.IsTrue(success);
        Assert.IsTrue(stateProfile != null);

        (success, message, stateProfile) = spec.AllowsConcurrent(
            stateProfile,
            new (IOperation operation, object request, object response)[]
            {
                (spec.Mirror, 1, 1),
                (spec.Mirror, 2, 3),
            });

        Assert.IsFalse(success);
        Assert.IsTrue(message != null && message.Length > 0);
        Assert.IsTrue(stateProfile == null);
    }

    [Test]
    public async Task AddOnlyListTest()
    {
        var spec = new AddOnlyListSpec();
        var initialState = new CounterListState();

        var operations = new InputSet()
        {
            spec.AddOp.With(1, "Add 1"),
            spec.Count.With("Count")
        };

        var context = spec.CreateTestingContext();
        var testCases = spec.GenerateTests(initialState, operations, new TestGenerationOptions { MaxDepth = 5 });
        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEach = _ => context.Register(new AddOnlyList())
            });

        Assert.IsTrue(results.All(r => r.Success), "Some test cases failed.");
    }

    [Test]
    public async Task BlogPostServiceTest()
    {
        var spec = new BlogPostServiceSpec();
        var initialState = new BlogPostsState();

        var operations = new InputSet()
        {
            spec.AddOp.With(
                new BlogPost()
                {
                    Name = "Hello",
                    Content = "World"
                },
                "Add")
        };

        // Run sequential tests
        var sequentialContext = spec.CreateTestingContext();
        var sequentialTestCases = spec.GenerateTests(initialState, operations, new TestGenerationOptions { MaxDepth = 5 });
        var sequentialResults = await spec.RunTests(
            sequentialContext,
            initialState,
            sequentialTestCases,
            new TestExecutionOptions
            {
                BeforeEach = _ => sequentialContext.Register(new BlogPostService())
            });
        Assert.IsTrue(sequentialResults.All(r => r.Success), "Some sequential test cases failed.");

        // Run concurrent tests
        var concurrentContext = spec.CreateTestingContext();
        var concurrentTestCases = spec.GenerateConcurrentTests(initialState, operations, new TestGenerationOptions { MaxDepth = 5 });
        var concurrentResults = await spec.RunTests(
            concurrentContext,
            initialState,
            concurrentTestCases,
            new TestExecutionOptions
            {
                BeforeEach = _ => concurrentContext.Register(new BlogPostService())
            });
        Assert.IsTrue(concurrentResults.All(r => r.Success), "Some concurrent test cases failed.");
    }

    [Test]
    public async Task BlogPostServiceTestCaseGenerationTest()
    {
        IList<SequentialTestCase> GenerateSequentialTestCases(
            BlogPostServiceSpec spec,
            IList<DerivationSelector> derivationSelectors = null)
        {
            var operations = new InputSet()
            {
                spec.AddOp.With(
                    new BlogPost()
                    {
                        Name = "Hello",
                        Content = "World"
                    },
                    "Add")
            };

            var startingState = new BlogPostsState();

            var dotFileContents = spec.VisualizeStateSpace(
                startingState,
                operations,
                generationOptions: new TestGenerationOptions()
                {
                    MaxDepth = 5,
                    DerivationSelectors = derivationSelectors
                });

            var sequentialTestCases = spec.GenerateTests(
                startingState,
                operations,
                new TestGenerationOptions()
                {
                    MaxDepth = 5,
                    DerivationSelectors = derivationSelectors
                });

            return sequentialTestCases;
        }

        {
            var testCases = GenerateSequentialTestCases(
                new BlogPostServiceSpec(),
                derivationSelectors: Array.Empty<DerivationSelector>());

            Assert.IsTrue(testCases.All(tc => !tc.Description.Contains("->")));
        }

        {
            var testCases = GenerateSequentialTestCases(
                new BlogPostServiceSpec(),
                derivationSelectors: null);

            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Delete)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-1)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-2)")));
        }

        {
            var testCases = GenerateSequentialTestCases(
                new BlogPostServiceSpec(),
                derivationSelectors: null);

            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Delete)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-1)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-2)")));
        }

        {
            var testCases = GenerateSequentialTestCases(
                new BlogPostServiceSpec(),
                derivationSelectors: new DerivationSelector[]
                {
                    DerivationSelector.For("Delete")
                });

            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Delete)")));
            Assert.IsTrue(!testCases.All(tc => tc.Description.Contains("Add -> Update)")));
            Assert.IsTrue(!testCases.All(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-1)")));
            Assert.IsTrue(!testCases.All(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-2)")));
        }

        {
            var testCases = GenerateSequentialTestCases(
                new BlogPostServiceSpec(),
                derivationSelectors: new DerivationSelector[]
                {
                    DerivationSelector.For("Update")
                });

            Assert.IsTrue(!testCases.All(tc => tc.Description.Contains("Add -> Delete)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-1)")));
            Assert.IsTrue(testCases.Any(tc => tc.Description.Contains("Add -> Update) -> Update: Alt-2)")));
        }
    }

    #region Simple Operations (mirror of SimpleBehaviors)

    public class SimpleOperations
    {
        public class SimpleSpec : Spec<CounterState>
        {
            public MirrorOperation Mirror { get; } = new();

            public SimpleSpec()
            {
                RegisterOperationProperties();
            }
        }

        public class MirrorOperation : Operation<int, int, CounterState>
        {
            public MirrorOperation() : base("Mirror") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                return new ExpectedOutcome(
                    Descriptor.FromValue(request),
                    state);
            }
        }
    }

    #endregion

    #region AddOnlyList (mirror of AddOnlyListBehaviors)

    public class AddOnlyList
    {
        private List<int> list = new List<int>();

        public void Add(int i)
        {
            list.Add(i);
        }

        public int Count()
        {
            return list.Count;
        }
    }

    public class AddOnlyListSpec : Spec<CounterListState>
    {
        public AddOperation AddOp { get; } = new();
        public CountOperation Count { get; } = new();

        public AddOnlyListSpec()
        {
            RegisterOperationProperties();
        }
    }

    public class AddOperation : Operation<int, Unit, CounterListState>
    {
        public AddOperation() : base("Add") { }

        public override ExpectedOutcomes Apply(int request, CounterListState state)
        {
            var updatedState = (CounterListState)state.Clone();
            updatedState.Items.Add(new CounterState(request));

            return new ExpectedOutcome(
                Descriptor.FromValue(Unit.Value),
                updatedState);
        }

        public override Unit Execute(TestingContext context, int request)
        {
            context.Get<AddOnlyList>().Add(request);
            return Unit.Value;
        }
    }

    public class CountOperation : Operation<Unit, int, CounterListState>
    {
        public CountOperation() : base("Count") { }

        public override ExpectedOutcomes Apply(Unit request, CounterListState state)
        {
            return new ExpectedOutcome(
                Descriptor.FromValue(state.Items.Count),
                state);
        }

        public override int Execute(TestingContext context, Unit request)
        {
            return context.Get<AddOnlyList>().Count();
        }
    }

    #endregion

    #region BlogPostService (mirror of BlogPostServiceBehaviors)

    public class BlogPost
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Content { get; set; }
    }

    public class BlogPostService
    {
        private Dictionary<string, BlogPost> blogPostMap = new Dictionary<string, BlogPost>();

        public string AddBlogPost(BlogPost blogPost)
        {
            lock (this)
            {
                var id = Guid.NewGuid().ToString();

                blogPostMap[id] = new BlogPost()
                {
                    Id = id,
                    Name = blogPost.Name,
                    Content = blogPost.Content
                };

                return id;
            }
        }

        public bool UpdateBlogPost(BlogPost blogPost)
        {
            lock (this)
            {
                var id = blogPost.Id;

                if (id == null ||
                    !blogPostMap.ContainsKey(id))
                {
                    return false;
                }

                blogPostMap[id].Name = blogPost.Name;
                blogPostMap[id].Content = blogPost.Content;

                return true;
            }
        }

        public BlogPost GetBlogPost(string id)
        {
            lock (this)
            {
                if (!blogPostMap.ContainsKey(id))
                {
                    return null;
                }

                return blogPostMap[id];
            }
        }

        public bool DeleteBlogPost(string id)
        {
            lock (this)
            {
                if (blogPostMap.ContainsKey(id))
                {
                    blogPostMap.Remove(id);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }

    public class BlogPostServiceSpec : Spec<BlogPostsState>
    {
        public AddBlogPostOperation AddOp { get; } = new();
        public UpdateBlogPostOperation Update { get; } = new();
        public DeleteBlogPostOperation Delete { get; } = new();

        public BlogPostServiceSpec()
        {
            RegisterOperationProperties();
        }
    }

    public class AddBlogPostOperation : Operation<BlogPost, string, BlogPostsState>
    {
        public AddBlogPostOperation() : base("Add") { }

        public override ExpectedOutcomes Apply(BlogPost request, BlogPostsState state)
        {
            return new ExpectedOutcome(
                ResponseValidator.FromPredicate<string>(v => Guid.TryParse(v, out _)),
                (response) =>
                {
                    var id = (string)response;

                    var updatedState = (BlogPostsState)state.Clone();

                    updatedState.Posts[id] = new BlogPostEntryState
                    {
                        Name = request.Name,
                        Content = request.Content
                    };

                    return updatedState;
                },
                mockResponse: () => Guid.NewGuid().ToString());
        }

        public override Task<string> ExecuteAsync(TestingContext context, BlogPost request)
        {
            var service = context.Get<BlogPostService>();

            var id = service.AddBlogPost(request);
            return Task.FromResult(id);
        }
    }

    public class GetBlogPostOperation : Operation<string, BlogPost, BlogPostsState>
    {
        public GetBlogPostOperation() : base("Get") { }

        public override ExpectedOutcomes Apply(string id, BlogPostsState state)
        {
            if (!state.Posts.ContainsKey(id))
            {
                return new ExpectedOutcome(
                    Descriptor.FromValue<BlogPost>(null),
                    state);
            }

            var blogPostState = state.Posts[id];

            return new ExpectedOutcome(
                Descriptor.FromValue(new BlogPost()
                {
                    Id = id,
                    Name = blogPostState.Name,
                    Content = blogPostState.Content
                }),
                state);
        }

        public override Task<BlogPost> ExecuteAsync(TestingContext context, string id)
        {
            var service = context.Get<BlogPostService>();

            var blogPost = service.GetBlogPost(id);
            return Task.FromResult(blogPost);
        }
    }

    public class UpdateBlogPostOperation : Operation<BlogPost, bool, BlogPostsState>
    {
        public UpdateBlogPostOperation() : base("Update") { }

        public override ExpectedOutcomes Apply(BlogPost request, BlogPostsState state)
        {
            var id = request.Id;

            if (id == null ||
                !state.Posts.ContainsKey(id))
            {
                return new ExpectedOutcome(
                    Descriptor.FromValue(false),
                    state);
            }

            var updatedState = (BlogPostsState)state.Clone();
            updatedState.Posts[id].Name = request.Name;
            updatedState.Posts[id].Content = request.Content;

            return new ExpectedOutcome(
                Descriptor.FromValue(true),
                updatedState);
        }

        public override Task<bool> ExecuteAsync(TestingContext context, BlogPost request)
        {
            var service = context.Get<BlogPostService>();

            var result = service.UpdateBlogPost(request);
            return Task.FromResult(result);
        }

        public override IReadOnlyList<RequestDerivation> DerivedFrom { get; } = new[]
        {
            Derive.From<BlogPost, string, BlogPost>("Add")
                  .As((request, response) => new BlogPost()
                  {
                      Id = response,
                      Name = request.Name,
                      Content = "Updated " + request.Content
                  }),
            Derive.From<BlogPost, bool, BlogPost>("Update")
                  .AsVariants((request, response) => new Dictionary<string, BlogPost>()
                  {
                      ["Alt-1"] = new BlogPost()
                      {
                          Id = request.Id,
                          Name = request.Name,
                          Content = "Updated " + request.Content
                      },
                      ["Alt-2"] = new BlogPost()
                      {
                          Id = request.Id,
                          Name = request.Name,
                          Content = "Another Updated " + request.Content
                      }
                  })
        };
    }

    public class DeleteBlogPostOperation : Operation<string, bool, BlogPostsState>
    {
        public DeleteBlogPostOperation() : base("Delete") { }

        public override ExpectedOutcomes Apply(string id, BlogPostsState state)
        {
            if (!state.Posts.ContainsKey(id))
            {
                return new ExpectedOutcome(
                    Descriptor.FromValue(false),
                    state);
            }

            var updatedState = (BlogPostsState)state.Clone();
            updatedState.Posts.Remove(id);

            return new ExpectedOutcome(
                Descriptor.FromValue(true),
                updatedState);
        }

        public override Task<bool> ExecuteAsync(TestingContext context, string request)
        {
            var service = context.Get<BlogPostService>();

            var result = service.DeleteBlogPost(request);
            return Task.FromResult(result);
        }

        public override IReadOnlyList<RequestDerivation> DerivedFrom { get; } = new[]
        {
            Derive.From<BlogPost, string, string>("Add")
                  .As((request, response) => response)
        };
    }

    #endregion

    #region Predicate-based ExpectedOutcome Tests (mirror of PredicateBehaviors)

    [Test]
    public void PredicateBasedExpectedOutcome_SimpleBoolTrue_ValidationPasses()
    {
        var spec = new PredicateOperations.PredicateSpec();
        var state = new CounterState(42);
        var stateProfile = new StateProfile(state);

        (var success, var message, var resultProfile) = spec.Allows(
            spec.MirrorWithPredicate,
            5,
            5,
            stateProfile);

        Assert.IsTrue(success, message);
        Assert.IsNotNull(resultProfile);
    }

    [Test]
    public void PredicateBasedExpectedOutcome_SimpleBoolFalse_ValidationFails()
    {
        var spec = new PredicateOperations.PredicateSpec();
        var state = new CounterState(42);
        var stateProfile = new StateProfile(state);

        (var success, var message, var resultProfile) = spec.Allows(
            spec.MirrorWithPredicate,
            5,
            10, // Wrong response
            stateProfile);

        Assert.IsFalse(success);
        Assert.IsNull(resultProfile);
    }

    [Test]
    public void PredicateBasedExpectedOutcome_ValidationResultInvalid_MessagePropagates()
    {
        var spec = new PredicateOperations.PredicateSpec();
        var state = new CounterState(42);
        var stateProfile = new StateProfile(state);

        (var success, var message, var resultProfile) = spec.Allows(
            spec.MirrorWithExplanation,
            5,
            10, // Wrong response
            stateProfile);

        Assert.IsFalse(success);
        Assert.IsTrue(message.Contains("Expected 5 but got 10"));
        Assert.IsNull(resultProfile);
    }

    [Test]
    public void PredicateBasedExpectedOutcome_ResponseDependentState_WorksCorrectly()
    {
        var spec = new PredicateOperations.PredicateSpec();
        var state = new CounterState(0);
        var stateProfile = new StateProfile(state);

        (var success, var message, var resultProfile) = spec.Allows(
            spec.UpdateStateFromResponse,
            "ignored",
            42,
            stateProfile);

        Assert.IsTrue(success, message);
        Assert.IsNotNull(resultProfile);

        // Verify the state was updated based on the response
        var resultState = (CounterState)resultProfile.SingleState();
        Assert.AreEqual(42, resultState.Value);
    }

    [Test]
    public void PredicateBasedExpectedOutcome_MultipleOutcomes_MatchesCorrectOne()
    {
        var spec = new PredicateOperations.PredicateSpec();
        var state = new CounterState(0);
        var stateProfile = new StateProfile(state);

        // Test positive response
        (var success, var message, var resultProfile) = spec.Allows(
            spec.MultipleOutcomes,
            10,
            10,
            stateProfile);

        Assert.IsTrue(success, message);
        var resultState = (CounterState)resultProfile.SingleState();
        Assert.AreEqual(10, resultState.Value);

        // Test negative response
        (success, message, resultProfile) = spec.Allows(
            spec.MultipleOutcomes,
            -5,
            -5,
            stateProfile);

        Assert.IsTrue(success, message);
        resultState = (CounterState)resultProfile.SingleState();
        Assert.AreEqual(0, resultState.Value); // Negative stays at 0
    }

    public class PredicateOperations
    {
        public class PredicateSpec : Spec<CounterState>
        {
            public MirrorWithPredicateOperation MirrorWithPredicate { get; } = new();
            public MirrorWithExplanationOperation MirrorWithExplanation { get; } = new();
            public UpdateStateFromResponseOperation UpdateStateFromResponse { get; } = new();
            public MultipleOutcomesOperation MultipleOutcomes { get; } = new();

            public PredicateSpec()
            {
                RegisterOperationProperties();
            }
        }

        /// <summary>
        /// Simple operation that expects response to equal request, using bool predicate.
        /// </summary>
        public class MirrorWithPredicateOperation : Operation<int, int, CounterState>
        {
            public MirrorWithPredicateOperation() : base("MirrorWithPredicate") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                return new ExpectedOutcome(
                    ResponseValidator.FromPredicate<int>(response => response == request),
                    state);
            }
        }

        /// <summary>
        /// Operation that provides explanation on validation failure.
        /// </summary>
        public class MirrorWithExplanationOperation : Operation<int, int, CounterState>
        {
            public MirrorWithExplanationOperation() : base("MirrorWithExplanation") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                return new ExpectedOutcome(
                    ResponseValidator.FromPredicate<int>(response => response == request
                        ? ValidationResult.Valid()
                        : ValidationResult.Invalid($"Expected {request} but got {response}")),
                    state);
            }
        }

        /// <summary>
        /// Operation that updates state based on the response value.
        /// </summary>
        public class UpdateStateFromResponseOperation : Operation<string, int, CounterState>
        {
            public UpdateStateFromResponseOperation() : base("UpdateStateFromResponse") { }

            public override ExpectedOutcomes Apply(string request, CounterState state)
            {
                return new ExpectedOutcome(
                    ResponseValidator.FromPredicate<int>(response => response > 0),
                    (object response) => new CounterState((int)response),
                    mockResponse: () => 100);
            }
        }

        /// <summary>
        /// Operation with multiple possible outcomes using predicates.
        /// </summary>
        public class MultipleOutcomesOperation : Operation<int, int, CounterState>
        {
            public MultipleOutcomesOperation() : base("MultipleOutcomes") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                return new ExpectedOutcomes(
                    // Positive case: response equals request, state updated
                    new ExpectedOutcome(
                        ResponseValidator.FromPredicate<int>(response => response == request && response >= 0),
                        (object response) => new CounterState((int)response),
                        mockResponse: () => request >= 0 ? request : 0),
                    // Negative case: response equals request, state stays at 0
                    new ExpectedOutcome(
                        ResponseValidator.FromPredicate<int>(response => response == request && response < 0),
                        new CounterState(0)));
            }
        }
    }

    #endregion

    #region Spec Bug Exception Tests

    /// <summary>
    /// Tests that Allows throws InvalidSpecException when the spec's Apply method throws.
    /// </summary>
    [Test]
    public void Allows_WhenSpecApplyThrows_ThrowsInvalidSpecException()
    {
        var spec = new BuggyOperations.BuggySpec();
        var stateProfile = new StateProfile(new CounterState(1));

        var ex = Assert.Throws<InvalidSpecException>(() =>
        {
            spec.Allows(
                spec.BuggyOperation,
                1,
                1,
                stateProfile);
        });

        Assert.IsInstanceOf<StepFunctionApplicationException>(ex.InnerException);
    }

    /// <summary>
    /// Tests that Allows returns false with a message when the response doesn't match (not a spec bug).
    /// </summary>
    [Test]
    public void Allows_WhenResponseMismatch_ReturnsFalseWithMessage()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var stateProfile = new StateProfile(new CounterState(1));

        (var success, var message, var nextStateProfile) = spec.Allows(
            spec.Mirror,
            1,
            999, // Wrong response - should be 1
            stateProfile);

        Assert.IsFalse(success);
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Length > 0);
        Assert.IsNull(nextStateProfile);
    }

    /// <summary>
    /// Tests that AllowsConcurrent throws InvalidSpecException when the spec's Apply method throws.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WhenSpecApplyThrows_ThrowsInvalidSpecException()
    {
        var spec = new BuggyOperations.BuggySpec();
        var stateProfile = new StateProfile(new CounterState(1));

        var ex = Assert.Throws<InvalidSpecException>(() =>
        {
            spec.AllowsConcurrent(
                stateProfile,
                new (IOperation operation, object request, object response)[]
                {
                    (spec.BuggyOperation, 1, 1),
                });
        });

        Assert.IsInstanceOf<StepFunctionApplicationException>(ex.InnerException);
    }

    /// <summary>
    /// Tests that AllowsConcurrent returns false with a message when responses don't match.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WhenResponseMismatch_ReturnsFalseWithMessage()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var stateProfile = new StateProfile(new CounterState(1));

        (var success, var message, var nextStateProfile) = spec.AllowsConcurrent(
            stateProfile,
            new (IOperation operation, object request, object response)[]
            {
                (spec.Mirror, 1, 999), // Wrong response
            });

        Assert.IsFalse(success);
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Length > 0);
        Assert.IsNull(nextStateProfile);
    }

    /// <summary>
    /// Tests AllowsConcurrent with happens-before edges, enabling linearizability
    /// checking for partially ordered histories.
    ///
    /// Scenario: Write always succeeds and sets state; Read expects response == state.
    ///   State starts at Value=0.
    ///   A: Write(1), observed="ok"  (always succeeds)
    ///   B: Read(),  observed=1      (must see state=1)
    ///   C: Read(),  observed=1      (must see state=1)
    ///
    /// Without edge: [A,B,C] and [A,C,B] both work → true.
    /// With edge C→A: C must precede A, but C needs A's write → deadlock → false.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_PrunesInvalidOrderings()
    {
        var spec = new Spec<CounterState>();

        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        spec.Operation<Unit, int>("Read", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue(state.Value),
                state));

        var writeOp = spec.GetOperation<int, string>("Write");
        var readOp = spec.GetOperation<Unit, int>("Read");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),         // A: Write(1)
            (readOp, Unit.Value, 1),     // B: Read() observed=1
            (readOp, Unit.Value, 1),     // C: Read() observed=1
        };

        // Without edge: valid (A writes 1, both reads see 1)
        var (noEdgeValid, _, _) = spec.AllowsConcurrent(stateProfile, calls);
        Assert.IsTrue(noEdgeValid,
            "Without edge, [A,B,C] and [A,C,B] both work.");

        // With edge C→A: C must precede A, but C needs A's write → no valid ordering
        var (withEdgeValid, withEdgeMsg, _) = spec.AllowsConcurrent(
            stateProfile, calls,
            new[] { (2, 0) });  // C(index 2) → A(index 0)

        Assert.IsFalse(withEdgeValid,
            "With edge C→A, C must run first but needs state=1 from A. " +
            $"Got: {withEdgeMsg}");
    }

    /// <summary>
    /// Tests that a happens-before edge satisfied by all valid orderings
    /// doesn't change the result.
    ///
    /// Same Write/Read setup as above.
    /// Edge A→C is already true in every valid ordering (A must write before any Read sees 1),
    /// so the result is unchanged.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_RedundantEdgeDoesNotChangeResult()
    {
        var spec = new Spec<CounterState>();

        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        spec.Operation<Unit, int>("Read", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue(state.Value),
                state));

        var writeOp = spec.GetOperation<int, string>("Write");
        var readOp = spec.GetOperation<Unit, int>("Read");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),
            (readOp, Unit.Value, 1),
            (readOp, Unit.Value, 1),
        };

        var (withEdgeValid, _, nextProfile) = spec.AllowsConcurrent(
            stateProfile,
            calls,
            [(0, 2)]); // A→C (already true in all valid orderings)

        Assert.IsTrue(withEdgeValid,
            "Edge A→C is trivially satisfied — A must write before any Read sees 1.");
        Assert.IsNotNull(nextProfile);
    }

    /// <summary>
    /// Tests a transitive happens-before chain where one operation is both a
    /// predecessor and a dependent (A→B→C). This exercises the GUID stability fix:
    /// when B gets predecessor IDs wired, it must keep its original StepFunctionId
    /// so that C's reference to B remains valid.
    ///
    /// Scenario: Write sets state to request value; Read expects response == state.
    ///   State starts at Value=0.
    ///   A: Write(1), observed="ok"
    ///   B: Write(2), observed="ok"
    ///   C: Read(),  observed=2  (must see B's write, not A's)
    ///
    /// With edges A→B and B→C, only [A,B,C] is a valid ordering.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_TransitiveChain()
    {
        var spec = new Spec<CounterState>();

        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        spec.Operation<Unit, int>("Read", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue(state.Value),
                state));

        var writeOp = spec.GetOperation<int, string>("Write");
        var readOp = spec.GetOperation<Unit, int>("Read");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),         // A: Write(1)
            (writeOp, 2, "ok"),         // B: Write(2)
            (readOp, Unit.Value, 2),     // C: Read() observed=2
        };

        // With edges A→B and B→C: only [A,B,C] works.
        // B is both a predecessor (to C) and a dependent (of A); its GUID
        // must stay stable when predecessor IDs are wired.
        var (valid, _, nextProfile) = spec.AllowsConcurrent(
            stateProfile, calls,
            new[] { (0, 1), (1, 2) });  // A→B, B→C

        Assert.IsTrue(valid,
            "With edges A→B and B→C, [A,B,C] is a valid ordering.");
        Assert.IsNotNull(nextProfile);
    }

    /// <summary>
    /// Tests multiple happens-before predecessors for a single operation (A→C, B→C).
    /// C must wait for both A and B before it can be applied.
    ///
    /// Scenario: Write sets state; Read expects response == state.
    ///   State starts at Value=0.
    ///   A: Write(1), observed="ok"
    ///   B: Write(2), observed="ok"
    ///   C: Read(),  observed=2  (must see B's write after A's)
    ///
    /// With edges A→C and B→C, valid orderings are [A,B,C] and [B,A,C].
    /// In both cases C sees state=2 (B overwrites A's value).
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_MultiplePredecessors()
    {
        var spec = new Spec<CounterState>();

        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        spec.Operation<Unit, int>("Read", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue(state.Value),
                state));

        var writeOp = spec.GetOperation<int, string>("Write");
        var readOp = spec.GetOperation<Unit, int>("Read");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),         // A: Write(1)
            (writeOp, 2, "ok"),         // B: Write(2)
            (readOp, Unit.Value, 2),     // C: Read() observed=2
        };

        var (valid, _, nextProfile) = spec.AllowsConcurrent(
            stateProfile, calls,
            new[] { (0, 2), (1, 2) });  // A→C, B→C

        Assert.IsTrue(valid,
            "With edges A→C and B→C, both [A,B,C] and [B,A,C] are valid.");
        Assert.IsNotNull(nextProfile);
    }

    /// <summary>
    /// Tests that a self-loop edge throws ArgumentException.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_SelfLoopThrows()
    {
        var spec = new Spec<CounterState>();
        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        var writeOp = spec.GetOperation<int, string>("Write");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            spec.AllowsConcurrent(stateProfile, calls, new[] { (0, 0) }));

        Assert.That(ex.Message, Does.Contain("Self-loop"));
    }

    /// <summary>
    /// Tests that a cycle in happens-before edges throws early, rather than
    /// letting the linearization search fail with a misleading "no ordering" message.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_CycleThrows()
    {
        var spec = new Spec<CounterState>();
        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        var writeOp = spec.GetOperation<int, string>("Write");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),
            (writeOp, 2, "ok"),
            (writeOp, 3, "ok"),
        };

        // 0 -> 1 -> 2 -> 0 is a cycle.
        var ex = Assert.Throws<ArgumentException>(() =>
            spec.AllowsConcurrent(stateProfile, calls, new[] { (0, 1), (1, 2), (2, 0) }));

        Assert.That(ex.Message, Does.Contain("Cycle"));
    }

    #region Kahn's algorithm cycle-detection tests

    /// <summary>
    /// Runs AllowsConcurrent with N idempotent no-op calls and the given edges.
    /// The operation returns a fixed response and leaves state unchanged, so any
    /// acyclic edge set linearizes. A "Cycle" ArgumentException therefore comes
    /// purely from Kahn's, not from linearization failure.
    /// </summary>
    private static ArgumentException RunHappensBefore(int n, params (int, int)[] edges)
    {
        var spec = new Spec<CounterState>();
        spec.Operation<int, string>("Noop", (request, state) =>
            new ExpectedOutcome(Descriptor.FromValue("ok"), state));

        var op = spec.GetOperation<int, string>("Noop");
        var stateProfile = new StateProfile(new CounterState(0));
        var calls = new (IOperation, object, object)[n];
        for (int i = 0; i < n; i++) calls[i] = (op, i, "ok");

        try
        {
            var (valid, _, _) = spec.AllowsConcurrent(stateProfile, calls, edges);
            Assert.IsTrue(valid);
            return null;
        }
        catch (ArgumentException ex) { return ex; }
    }

    [Test]
    public void Kahn_Diamond_IsAcyclic()
    {
        // 0→1, 0→2, 1→3, 2→3. Node 3 has in-degree 2.
        // Catches enqueue-on-first-decrement bugs and reversed-edge-direction bugs.
        Assert.IsNull(RunHappensBefore(4, (0, 1), (0, 2), (1, 3), (2, 3)));
    }

    [Test]
    public void Kahn_TwoNodeCycle_IsDetected()
    {
        // Smallest non-self cycle. Catches implementations that only see self-loops.
        var ex = RunHappensBefore(2, (0, 1), (1, 0));
        Assert.That(ex?.Message, Does.Contain("Cycle"));
    }

    [Test]
    public void Kahn_CycleWithTail_ReportsOnlyCycleMembers()
    {
        // 0→1→2→3, 3→1. Node 0 drains; {1,2,3} stay stuck.
        // Catches missed cycles AND wrongly blamed drainable nodes.
        var ex = RunHappensBefore(4, (0, 1), (1, 2), (2, 3), (3, 1));
        Assert.That(ex?.Message, Does.Contain("Cycle"));

        var start = ex.Message.IndexOf('[');
        var end = ex.Message.IndexOf(']');
        var listed = ex.Message.Substring(start + 1, end - start - 1)
            .Split(',').Select(s => s.Trim()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "1", "2", "3" }, listed);
    }

    #endregion

    /// <summary>
    /// Tests that an out-of-range happens-before index throws ArgumentException.
    /// </summary>
    [Test]
    public void AllowsConcurrent_WithHappensBefore_OutOfRangeIndexThrows()
    {
        var spec = new Spec<CounterState>();
        spec.Operation<int, string>("Write", (request, state) =>
            new ExpectedOutcome(
                Descriptor.FromValue("ok"),
                new CounterState(request)));

        var writeOp = spec.GetOperation<int, string>("Write");
        var stateProfile = new StateProfile(new CounterState(0));

        var calls = new (IOperation, object, object)[]
        {
            (writeOp, 1, "ok"),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            spec.AllowsConcurrent(stateProfile, calls, new[] { (0, 1) }));

        Assert.That(ex.Message, Does.Contain("outside the range"));
    }

    /// <summary>
    /// Tests that ExplainInvalidResponse path throws when spec has a bug.
    /// When response doesn't match and Apply throws during explanation.
    /// Note: ExplainInvalidResponse calls Apply directly (not through SystemChecker.Validate),
    /// so the raw exception propagates without being wrapped in InvalidSpecException.
    /// </summary>
    [Test]
    public void Allows_WhenExplainInvalidResponseThrows_ThrowsRawException()
    {
        var spec = new BuggyOperations.BuggyOnSecondCallSpec();
        var stateProfile = new StateProfile(new CounterState(1));

        // First Apply call succeeds but returns non-matching result,
        // Second Apply call (during ExplainInvalidResponse) throws
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            spec.Allows(
                spec.BuggyOnSecondCall,
                1,
                999, // Wrong response - triggers ExplainInvalidResponse path
                stateProfile);
        });

        Assert.IsTrue(ex.Message.Contains("Bug in spec"));
    }

    public class BuggyOperations
    {
        public class BuggySpec : Spec<CounterState>
        {
            public BuggyOperation BuggyOperation { get; } = new();

            public BuggySpec()
            {
                RegisterOperationProperties();
            }
        }

        /// <summary>
        /// An operation that always throws in Apply - simulates a spec bug.
        /// </summary>
        public class BuggyOperation : Operation<int, int, CounterState>
        {
            public BuggyOperation() : base("Buggy") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                throw new InvalidOperationException("Bug in spec's Apply method");
            }
        }

        public class BuggyOnSecondCallSpec : Spec<CounterState>
        {
            public BuggyOnSecondCallOperation BuggyOnSecondCall { get; } = new();

            public BuggyOnSecondCallSpec()
            {
                RegisterOperationProperties();
            }
        }

        /// <summary>
        /// An operation that throws on the second Apply call.
        /// Used to test the ExplainInvalidResponse code path.
        /// </summary>
        public class BuggyOnSecondCallOperation : Operation<int, int, CounterState>
        {
            private int callCount = 0;

            public BuggyOnSecondCallOperation() : base("BuggyOnSecondCall") { }

            public override ExpectedOutcomes Apply(int request, CounterState state)
            {
                callCount++;
                if (callCount > 1)
                {
                    throw new InvalidOperationException("Bug in spec's Apply method on second call");
                }

                // Return a valid expectation that won't match the test response
                return new ExpectedOutcome(
                    Descriptor.FromValue(request),
                    state);
            }
        }
    }

    #endregion

    #region Derive.From().When() Tests

    [Test]
    public void DeriveFrom_When_As_FilterPasses_ProducesDerivation()
    {
        // Arrange: Create a derivation with a When() filter that passes
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 0)
            .As((req, resp) => $"{req}-{resp}");

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources);

        // Assert
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("hello-42", results[DerivationLabels.Default]);
    }

    [Test]
    public void DeriveFrom_When_As_FilterFails_ReturnsEmpty()
    {
        // Arrange: Create a derivation with a When() filter that fails
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 100) // Will fail: 42 is not > 100
            .As((req, resp) => $"{req}-{resp}");

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources);

        // Assert: Should return empty dictionary (skip derivation)
        Assert.AreEqual(0, results.Count);
    }

    [Test]
    public void DeriveFrom_When_AsVariants_FilterPasses_ProducesAllVariants()
    {
        // Arrange: Create a derivation with When() and AsVariants()
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 0)
            .AsVariants((req, resp) => new Dictionary<string, string>
            {
                ["first"] = $"{req}-first-{resp}",
                ["second"] = $"{req}-second-{resp}"
            });

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources);

        // Assert
        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("hello-first-42", results["first"]);
        Assert.AreEqual("hello-second-42", results["second"]);
    }

    [Test]
    public void DeriveFrom_When_AsVariants_FilterFails_ReturnsEmpty()
    {
        // Arrange: Create a derivation with When() and AsVariants() where filter fails
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 100) // Will fail
            .AsVariants((req, resp) => new Dictionary<string, string>
            {
                ["first"] = $"{req}-first-{resp}",
                ["second"] = $"{req}-second-{resp}"
            });

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources);

        // Assert: Should return empty dictionary
        Assert.AreEqual(0, results.Count);
    }

    [Test]
    public void DeriveFrom_When_As_WithTemplate_FilterPasses_Works()
    {
        // Arrange: Create a derivation with When(), As(), and template
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 0)
            .As((req, resp, template) => $"{template}-{req}-{resp}");

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources, "prefix");

        // Assert
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("prefix-hello-42", results[DerivationLabels.Default]);
    }

    [Test]
    public void DeriveFrom_When_AsVariants_WithTemplate_FilterPasses_Works()
    {
        // Arrange
        var derivation = Derive.From<string, int, string>("Source")
            .When((req, resp) => resp > 0)
            .AsVariants((req, resp, template) => new Dictionary<string, string>
            {
                ["a"] = $"{template}-a-{resp}",
                ["b"] = $"{template}-b-{resp}"
            });

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", 42)
        };

        // Act
        var results = derivation.Derive(sources, "prefix");

        // Assert
        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("prefix-a-42", results["a"]);
        Assert.AreEqual("prefix-b-42", results["b"]);
    }

    [Test]
    public void DeriveFrom_When_HandlesNullResponse_Gracefully()
    {
        // Arrange: When() filter checks for null
        var derivation = Derive.From<string, string, string>("Source")
            .When((req, resp) => resp != null && resp.Length > 0)
            .As((req, resp) => $"{req}-{resp}");

        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = ("hello", (object)null)
        };

        // Act
        var results = derivation.Derive(sources);

        // Assert: Filter returns false for null, so empty
        Assert.AreEqual(0, results.Count);
    }

    #endregion

    #region ConfigureDerivations Tests

    [Test]
    public void ConfigureDerivations_SetsDerivationsOnInlineOperation()
    {
        // Arrange
        var spec = new Spec<CounterState>();

        // Add an inline operation
        spec.Operation<int, int>("Source", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 2), state));

        spec.Operation<int, int>("Derived", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req + 1), state));

        // Act: Configure derivations
        spec.ConfigureDerivations("Derived",
            Derive.From<int, int, int>("Source")
                .When((req, resp) => resp > 0)
                .As((req, resp) => resp));

        // Assert: Check that the operation has the derivation
        var derivedOp = spec.Operations.First(op => op.Name == "Derived");
        Assert.AreEqual(1, derivedOp.DerivedFrom.Count);
        Assert.Contains("Source", derivedOp.DerivedFrom[0].Sources.ToList());
    }

    [Test]
    public void ConfigureDerivations_ThrowsForNonExistentOperation()
    {
        // Arrange
        var spec = new Spec<CounterState>();

        spec.Operation<int, int>("Source", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 2), state));

        // Act & Assert
        Assert.Throws<SpecException>(() =>
        {
            spec.ConfigureDerivations("NonExistent",
                Derive.From<int, int, int>("Source").As((req, resp) => resp));
        });
    }

    [Test]
    public void ConfigureDerivations_WorksWithMultipleDerivations()
    {
        // Arrange
        var spec = new Spec<CounterState>();

        spec.Operation<int, int>("Source1", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 2), state));

        spec.Operation<int, int>("Source2", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 3), state));

        spec.Operation<int, int>("Derived", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req + 1), state));

        // Act: Configure multiple derivations
        spec.ConfigureDerivations("Derived",
            Derive.From<int, int, int>("Source1").As((req, resp) => resp),
            Derive.From<int, int, int>("Source2").As((req, resp) => resp));

        // Assert
        var derivedOp = spec.Operations.First(op => op.Name == "Derived");
        Assert.AreEqual(2, derivedOp.DerivedFrom.Count);
    }

    [Test]
    public void ConfigureDerivations_DerivationsAreUsedDuringDerive()
    {
        // Arrange
        var spec = new Spec<CounterState>();

        spec.Operation<int, int>("Source", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 2), state));

        spec.Operation<int, int>("Derived", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req + 100), state));

        spec.ConfigureDerivations("Derived",
            Derive.From<int, int, int>("Source")
                .When((req, resp) => resp > 0)
                .As((req, resp) => resp + 10));

        // Act: Use the derivation
        var derivedOp = spec.Operations.First(op => op.Name == "Derived");
        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = (5, 10) // Source returned 10
        };

        var results = derivedOp.DerivedFrom[0].Derive(sources);

        // Assert: Should derive 10 + 10 = 20
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(20, results[DerivationLabels.Default]);
    }

    [Test]
    public void ConfigureDerivations_WithWhenFilter_SkipsWhenFilterFails()
    {
        // Arrange
        var spec = new Spec<CounterState>();

        spec.Operation<int, int>("Source", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req * 2), state));

        spec.Operation<int, int>("Derived", (req, state) =>
            new ExpectedOutcome(Descriptor.FromValue(req + 100), state));

        spec.ConfigureDerivations("Derived",
            Derive.From<int, int, int>("Source")
                .When((req, resp) => resp > 50) // Will fail for resp=10
                .As((req, resp) => resp + 10));

        // Act
        var derivedOp = spec.Operations.First(op => op.Name == "Derived");
        var sources = new Dictionary<string, (object Request, object Response)>
        {
            ["Source"] = (5, 10) // resp=10, which is not > 50
        };

        var results = derivedOp.DerivedFrom[0].Derive(sources);

        // Assert: Should return empty (filter failed)
        Assert.AreEqual(0, results.Count);
    }

    #endregion

    #region SkipPolling Tests

    [Test]
    public void WithoutPollingSetsFlagCorrectly()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var input = spec.Mirror.With(42);

        Assert.IsFalse(input.SkipPolling);

        var inputWithoutPolling = input.WithoutPolling();

        Assert.IsTrue(inputWithoutPolling.SkipPolling);
        Assert.AreSame(input, inputWithoutPolling); // Fluent API returns same instance
    }

    [Test]
    public void SkipPollingIsCopiedOnClone()
    {
        var spec = new SimpleOperations.SimpleSpec();
        var input = spec.Mirror.With(42).WithoutPolling();

        Assert.IsTrue(input.SkipPolling);

        var cloned = input.Clone();

        Assert.IsTrue(cloned.SkipPolling);
        Assert.AreNotSame(input, cloned);
    }

    #endregion
}
