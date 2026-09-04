using System.Net.Http.Json;
using System.Text.Json;
using Specmine;
using Specmine.Accordant;
using Specmine.Adapters.OpenApi;

namespace TaskWorkflow.ProcessExperiment1.Model;

internal static class Program
{
    private const string BaseUrl = "http://localhost:5000";

    public static async Task Main()
    {
        var evaluationRoot = Paths.GetEvaluationRoot();
        var tracesDirectory = Path.Combine(evaluationRoot, "traces");
        var summaryPath = Path.Combine(evaluationRoot, "replay-summary.json");
        var openApiPath = Paths.GetOpenApiPath(evaluationRoot);

        Directory.CreateDirectory(tracesDirectory);

        foreach (var existingTrace in Directory.EnumerateFiles(tracesDirectory, "*.json"))
        {
            File.Delete(existingTrace);
        }

        if (File.Exists(summaryPath))
        {
            File.Delete(summaryPath);
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
        };

        await EnsureHealthyAsync(httpClient);

        var adapterSettings = JsonSerializer.SerializeToElement(
            new { document = openApiPath, baseUrl = BaseUrl },
            ContractJson.SerializerOptions);

        await using var target = await new OpenApiTargetAdapter().ConnectAsync(adapterSettings);

        var recordedScenarios = new List<RecordedScenario>
        {
            await RecordScenarioAsync(
                "complete-happy-path-idempotent",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("  alpha  ")),
                        ContractJson.SerializerOptions);

                    var task = ResponseReaders.ReadTask(created.Body);
                    var request = new TaskByIdCall(new TaskPath(task.Id));

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CompleteTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CompleteTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        request,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "cancel-happy-path-idempotent",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("gamma")),
                        ContractJson.SerializerOptions);

                    var task = ResponseReaders.ReadTask(created.Body);
                    var request = new TaskByIdCall(new TaskPath(task.Id));

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CancelTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CancelTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        request,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "completed-then-cancel-conflict",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("beta")),
                        ContractJson.SerializerOptions);

                    var task = ResponseReaders.ReadTask(created.Body);
                    var request = new TaskByIdCall(new TaskPath(task.Id));

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CompleteTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CancelTask",
                        request,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "canceled-then-complete-conflict",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("delta")),
                        ContractJson.SerializerOptions);

                    var task = ResponseReaders.ReadTask(created.Body);
                    var request = new TaskByIdCall(new TaskPath(task.Id));

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CancelTask",
                        request,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CompleteTask",
                        request,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "blank-title-validation",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("   ")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "get-missing-task",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        new TaskByIdCall(new TaskPath("not-a-guid")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "complete-missing-task",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CompleteTask",
                        new TaskByIdCall(new TaskPath("not-a-guid")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "cancel-missing-task",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "CancelTask",
                        new TaskByIdCall(new TaskPath("not-a-guid")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "duplicate-title-fresh-ids",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var first = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("dup")),
                        ContractJson.SerializerOptions);

                    var second = await session.ExecuteAsync<CreateTaskCall, ApiCallResponse>(
                        "CreateTask",
                        new CreateTaskCall(new CreateTaskBody("dup")),
                        ContractJson.SerializerOptions);

                    var firstTask = ResponseReaders.ReadTask(first.Body);
                    var secondTask = ResponseReaders.ReadTask(second.Body);

                    if (string.Equals(firstTask.Id, secondTask.Id, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Expected duplicate-title creates to return distinct IDs.");
                    }

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        new TaskByIdCall(new TaskPath(firstTask.Id)),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<TaskByIdCall, ApiCallResponse>(
                        "GetTask",
                        new TaskByIdCall(new TaskPath(secondTask.Id)),
                        ContractJson.SerializerOptions);
                }),
        };

        var spec = TaskWorkflowSpec.Create();
        var replaySummaries = new List<object>();

        using (Understanding.UseStrictness(UnderstandingStrictness.Reject))
        {
            foreach (var scenario in recordedScenarios)
            {
                var replay = TraceReplayer.Replay(
                    spec,
                    new TaskWorkflowState(),
                    scenario.Trace,
                    ContractJson.SerializerOptions);

                if (replay.Status != TraceReplayStatus.Conforming)
                {
                    throw new InvalidOperationException(
                        $"Replay for scenario '{scenario.Name}' ended with status '{replay.Status}'.");
                }

                replaySummaries.Add(new
                {
                    scenario = scenario.Name,
                    traceId = scenario.Trace.TraceId,
                    tracePath = Path.GetRelativePath(evaluationRoot, scenario.TracePath),
                    replay.Status,
                    replay.AcceptedMatchCount,
                    replay.ProvisionalMatchCount,
                    replay.UnknownCount,
                    replay.OutOfScopeCount,
                    replay.ViolationCount,
                    steps = replay.Steps.Select(step => new
                    {
                        step.CallId,
                        step.OperationName,
                        step.Outcome,
                        step.Message,
                        step.MarkerId,
                    }),
                });
            }
        }

        await File.WriteAllTextAsync(
            summaryPath,
            JsonSerializer.Serialize(replaySummaries, ContractJson.IndentedSerializerOptions));

        Console.WriteLine($"Recorded {recordedScenarios.Count} traces in '{tracesDirectory}'.");
        Console.WriteLine($"Wrote replay summary to '{summaryPath}'.");
    }

    private static async Task EnsureHealthyAsync(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(ContractJson.SerializerOptions);
        if (!string.Equals(payload?.Status, "healthy", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TaskWorkflow health probe did not return status 'healthy'.");
        }
    }

    private static async Task ResetAsync(HttpClient httpClient)
    {
        var response = await httpClient.PostAsync("/__test/reset", content: null);
        if ((int)response.StatusCode != 204)
        {
            throw new InvalidOperationException(
                $"Reset failed. Expected HTTP 204 but received {(int)response.StatusCode}.");
        }
    }

    private static async Task<RecordedScenario> RecordScenarioAsync(
        string name,
        HttpClient httpClient,
        ITargetSession target,
        string tracesDirectory,
        Func<RecordingTargetSession, Task> body)
    {
        await ResetAsync(httpClient);
        var (trace, path) = await TraceRecorder.RunAsync(tracesDirectory, target, body);
        return new RecordedScenario(name, trace, path);
    }
}

internal sealed record RecordedScenario(string Name, RecordedTrace Trace, string TracePath);

internal static class Paths
{
    public static string GetEvaluationRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    public static string GetOpenApiPath(string evaluationRoot) =>
        Path.GetFullPath(Path.Combine(
            evaluationRoot,
            "..",
            "..",
            "..",
            "benchmarks",
            "TaskWorkflow",
            "TaskWorkflow.Api",
            "openapi.json"));
}
