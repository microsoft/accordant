// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using Microsoft.Accordant;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;

    /// <summary>
    /// Helper class to provide Descriptor-like API using ResponseValidator (no Descriptors dependency).
    /// </summary>
    internal static class Descriptor
    {
        public static ResponseValidator FromValue<T>(T expected)
        {
            return ResponseValidator.FromPredicate<T>(actual => Equals(actual, expected));
        }
    }

    /// <summary>
    /// Helper class to provide PredicateDescriptor-like API using ResponseValidator.
    /// </summary>
    internal class PredicateDescriptor<T>
    {
        private readonly Func<T, bool> _predicate;

        public PredicateDescriptor(Func<object, T, bool> predicate)
        {
            // Context is not used in this simplified version
            _predicate = value => predicate(null, value);
        }

        public static implicit operator ResponseValidator(PredicateDescriptor<T> descriptor)
        {
            return ResponseValidator.FromPredicate<T>(descriptor._predicate);
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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
                new AtomicState<int>(1),
                new AtomicState<int>(2)
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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
            var initialState = new ListState<AtomicState<int>>();

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
            var initialState = new DictionaryState<DictionaryState<AtomicState<string>>>();

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

                var startingState = new DictionaryState<DictionaryState<AtomicState<string>>>();

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
            public class SimpleSpec : Spec<AtomicState<int>>
            {
                public MirrorOperation Mirror { get; } = new();

                public SimpleSpec()
                {
                    RegisterOperationProperties();
                }
            }

            public class MirrorOperation : Operation<int, int, AtomicState<int>>
            {
                public MirrorOperation() : base("Mirror") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
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

        public class AddOnlyListSpec : Spec<ListState<AtomicState<int>>>
        {
            public AddOperation AddOp { get; } = new();
            public CountOperation Count { get; } = new();

            public AddOnlyListSpec()
            {
                RegisterOperationProperties();
            }
        }

        public class AddOperation : Operation<int, Unit, ListState<AtomicState<int>>>
        {
            public AddOperation() : base("Add") { }

            public override ExpectedOutcomes Apply(int request, ListState<AtomicState<int>> state)
            {
                var updatedState = (ListState<AtomicState<int>>)state.Clone();
                updatedState.Add(new AtomicState<int>(request));

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

        public class CountOperation : Operation<Unit, int, ListState<AtomicState<int>>>
        {
            public CountOperation() : base("Count") { }

            public override ExpectedOutcomes Apply(Unit request, ListState<AtomicState<int>> state)
            {
                return new ExpectedOutcome(
                    Descriptor.FromValue(state.Count),
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

        public class BlogPostServiceSpec : Spec<DictionaryState<DictionaryState<AtomicState<string>>>>
        {
            public AddBlogPostOperation AddOp { get; } = new();
            public UpdateBlogPostOperation Update { get; } = new();
            public DeleteBlogPostOperation Delete { get; } = new();

            public BlogPostServiceSpec()
            {
                RegisterOperationProperties();
            }
        }

        public class AddBlogPostOperation : Operation<BlogPost, string, DictionaryState<DictionaryState<AtomicState<string>>>>
        {
            public AddBlogPostOperation() : base("Add") { }

            public override ExpectedOutcomes Apply(BlogPost request, DictionaryState<DictionaryState<AtomicState<string>>> state)
            {
                return new ExpectedOutcome(
                    new PredicateDescriptor<string>((c, v) => Guid.TryParse(v, out _)),
                    (response) =>
                    {
                        var id = (string)response;

                        var updatedState = (DictionaryState<DictionaryState<AtomicState<string>>>)state.Clone();

                        updatedState[id] = new DictionaryState<AtomicState<string>>();
                        updatedState[id][nameof(BlogPost.Name)] =
                            new AtomicState<string>(request.Name);
                        updatedState[id][nameof(BlogPost.Content)] =
                            new AtomicState<string>(request.Content);

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

        public class GetBlogPostOperation : Operation<string, BlogPost, DictionaryState<DictionaryState<AtomicState<string>>>>
        {
            public GetBlogPostOperation() : base("Get") { }

            public override ExpectedOutcomes Apply(string id, DictionaryState<DictionaryState<AtomicState<string>>> state)
            {
                if (!state.ContainsKey(id))
                {
                    return new ExpectedOutcome(
                        Descriptor.FromValue<BlogPost>(null),
                        state);
                }

                var blogPostState = state[id];

                return new ExpectedOutcome(
                    Descriptor.FromValue(new BlogPost()
                    {
                        Id = id,
                        Name = blogPostState[nameof(BlogPost.Name)].Value,
                        Content = blogPostState[nameof(BlogPost.Content)].Value
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

        public class UpdateBlogPostOperation : Operation<BlogPost, bool, DictionaryState<DictionaryState<AtomicState<string>>>>
        {
            public UpdateBlogPostOperation() : base("Update") { }

            public override ExpectedOutcomes Apply(BlogPost request, DictionaryState<DictionaryState<AtomicState<string>>> state)
            {
                var id = request.Id;

                if (id == null ||
                    !state.ContainsKey(id))
                {
                    return new ExpectedOutcome(
                        Descriptor.FromValue(false),
                        state);
                }

                var updatedState = (DictionaryState<DictionaryState<AtomicState<string>>>)state.Clone();
                var updatedBlogPostState = updatedState[id];

                updatedBlogPostState[nameof(BlogPost.Name)] = new AtomicState<string>(request.Name);
                updatedBlogPostState[nameof(BlogPost.Content)] = new AtomicState<string>(request.Content);

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

        public class DeleteBlogPostOperation : Operation<string, bool, DictionaryState<DictionaryState<AtomicState<string>>>>
        {
            public DeleteBlogPostOperation() : base("Delete") { }

            public override ExpectedOutcomes Apply(string id, DictionaryState<DictionaryState<AtomicState<string>>> state)
            {
                if (!state.ContainsKey(id))
                {
                    return new ExpectedOutcome(
                        Descriptor.FromValue(false),
                        state);
                }

                var updatedState = (DictionaryState<DictionaryState<AtomicState<string>>>)state.Clone();
                updatedState.Remove(id);

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
            var state = new AtomicState<int>(42);
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
            var state = new AtomicState<int>(42);
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
            var state = new AtomicState<int>(42);
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
            var state = new AtomicState<int>(0);
            var stateProfile = new StateProfile(state);

            (var success, var message, var resultProfile) = spec.Allows(
                spec.UpdateStateFromResponse,
                "ignored",
                42,
                stateProfile);

            Assert.IsTrue(success, message);
            Assert.IsNotNull(resultProfile);

            // Verify the state was updated based on the response
            var resultState = (AtomicState<int>)resultProfile.SingleState();
            Assert.AreEqual(42, resultState.Value);
        }

        [Test]
        public void PredicateBasedExpectedOutcome_MultipleOutcomes_MatchesCorrectOne()
        {
            var spec = new PredicateOperations.PredicateSpec();
            var state = new AtomicState<int>(0);
            var stateProfile = new StateProfile(state);

            // Test positive response
            (var success, var message, var resultProfile) = spec.Allows(
                spec.MultipleOutcomes,
                10,
                10,
                stateProfile);

            Assert.IsTrue(success, message);
            var resultState = (AtomicState<int>)resultProfile.SingleState();
            Assert.AreEqual(10, resultState.Value);

            // Test negative response
            (success, message, resultProfile) = spec.Allows(
                spec.MultipleOutcomes,
                -5,
                -5,
                stateProfile);

            Assert.IsTrue(success, message);
            resultState = (AtomicState<int>)resultProfile.SingleState();
            Assert.AreEqual(0, resultState.Value); // Negative stays at 0
        }

        public class PredicateOperations
        {
            public class PredicateSpec : Spec<AtomicState<int>>
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
            public class MirrorWithPredicateOperation : Operation<int, int, AtomicState<int>>
            {
                public MirrorWithPredicateOperation() : base("MirrorWithPredicate") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
                {
                    return new ExpectedOutcome(
                        ResponseValidator.FromPredicate<int>(response => response == request),
                        state);
                }
            }

            /// <summary>
            /// Operation that provides explanation on validation failure.
            /// </summary>
            public class MirrorWithExplanationOperation : Operation<int, int, AtomicState<int>>
            {
                public MirrorWithExplanationOperation() : base("MirrorWithExplanation") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
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
            public class UpdateStateFromResponseOperation : Operation<string, int, AtomicState<int>>
            {
                public UpdateStateFromResponseOperation() : base("UpdateStateFromResponse") { }

                public override ExpectedOutcomes Apply(string request, AtomicState<int> state)
                {
                    return new ExpectedOutcome(
                        ResponseValidator.FromPredicate<int>(response => response > 0),
                        (object response) => new AtomicState<int>((int)response),
                        mockResponse: () => 100);
                }
            }

            /// <summary>
            /// Operation with multiple possible outcomes using predicates.
            /// </summary>
            public class MultipleOutcomesOperation : Operation<int, int, AtomicState<int>>
            {
                public MultipleOutcomesOperation() : base("MultipleOutcomes") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
                {
                    return new ExpectedOutcomes(
                        // Positive case: response equals request, state updated
                        new ExpectedOutcome(
                            ResponseValidator.FromPredicate<int>(response => response == request && response >= 0),
                            (object response) => new AtomicState<int>((int)response),
                            mockResponse: () => request >= 0 ? request : 0),
                        // Negative case: response equals request, state stays at 0
                        new ExpectedOutcome(
                            ResponseValidator.FromPredicate<int>(response => response == request && response < 0),
                            new AtomicState<int>(0)));
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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
        /// Tests that ExplainInvalidResponse path throws when spec has a bug.
        /// When response doesn't match and Apply throws during explanation.
        /// Note: ExplainInvalidResponse calls Apply directly (not through SystemChecker.Validate),
        /// so the raw exception propagates without being wrapped in InvalidSpecException.
        /// </summary>
        [Test]
        public void Allows_WhenExplainInvalidResponseThrows_ThrowsRawException()
        {
            var spec = new BuggyOperations.BuggyOnSecondCallSpec();
            var stateProfile = new StateProfile(new AtomicState<int>(1));

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
            public class BuggySpec : Spec<AtomicState<int>>
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
            public class BuggyOperation : Operation<int, int, AtomicState<int>>
            {
                public BuggyOperation() : base("Buggy") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
                {
                    throw new InvalidOperationException("Bug in spec's Apply method");
                }
            }

            public class BuggyOnSecondCallSpec : Spec<AtomicState<int>>
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
            public class BuggyOnSecondCallOperation : Operation<int, int, AtomicState<int>>
            {
                private int callCount = 0;

                public BuggyOnSecondCallOperation() : base("BuggyOnSecondCall") { }

                public override ExpectedOutcomes Apply(int request, AtomicState<int> state)
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
            var spec = new Spec<AtomicState<int>>();
            
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
            var spec = new Spec<AtomicState<int>>();
            
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
            var spec = new Spec<AtomicState<int>>();
            
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
            var spec = new Spec<AtomicState<int>>();
            
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
            var spec = new Spec<AtomicState<int>>();
            
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
}
