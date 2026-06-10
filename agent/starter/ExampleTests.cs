namespace KeyValueStore.Tests;

using Microsoft.Accordant;
using NUnit.Framework;

/// <summary>
/// Tests: wire the spec to the real system, provide inputs, run.
/// </summary>
[TestFixture]
public class KvTests
{
    [Test]
    public async Task GeneratedSequentialTests()
    {
        // 1. Create the spec
        var spec = KvSpec.Create();

        // 2. Bind operations to the real system
        //    (Replace KvClient with your actual client/service)
        spec.ExecuteWith<KvClient>()
            .BindAsync<PutRequest, ApiResult>("Put",
                (client, req) => client.PutAsync(req.Key, req.Value))
            .BindAsync<string, ApiResult<string>>("Get",
                (client, key) => client.GetAsync(key))
            .BindAsync<string, ApiResult>("Delete",
                (client, key) => client.DeleteAsync(key));

        // 3. Define inputs — Accordant explores all sequences of these
        var put = spec.GetOperation<PutRequest, ApiResult>("Put");
        var get = spec.GetOperation<string, ApiResult<string>>("Get");
        var delete = spec.GetOperation<string, ApiResult>("Delete");

        var inputs = new InputSet()
        {
            put.With(new PutRequest("key1", "hello"), "Put key1=hello"),
            put.With(new PutRequest("key1", "world"), "Put key1=world (overwrite)"),
            get.With("key1", "Get key1"),
            get.With("unknown", "Get unknown key"),
            delete.With("key1", "Delete key1"),
        };

        // 4. Generate test cases
        var initialState = new KvState();
        var testCases = spec.GenerateTests(initialState, inputs, new TestGenerationOptions
        {
            MaxDepth = 4  // Sequences up to 4 operations long
        });

        // 5. Create context and run tests
        var context = spec.CreateTestingContext();
        context.Register(new KvClient(/* your HttpClient or service reference */));

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async ctx =>
                {
                    // Reset state before each test case.
                    // Use whatever mechanism your system provides:
                    var client = ctx.Context.Get<KvClient>();
                    await client.DeleteAsync("key1");
                    await client.DeleteAsync("unknown");
                }
            });

        // 6. Check results
        var failures = results.Where(r => !r.Success).ToList();
        Assert.That(failures, Is.Empty,
            $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");

        TestContext.WriteLine($"Ran {results.Count} test cases — all passed!");
    }
}
