using System.Net.Http.Json;
using System.Text.Json;
using Specmine;
using Specmine.Accordant;
using Specmine.Adapters.OpenApi;

namespace PaymentProcessing.ProcessExperiment1.Model;

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

        var scenarios = new List<RecordedScenario>
        {
            await RecordScenarioAsync(
                "fresh-authorize-then-exact-replay",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-replay", 10m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-replay", 10m, "USD")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "successful-authorize-then-changed-amount-conflict",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-amount", 10m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-amount", 11m, "USD")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "successful-authorize-then-changed-currency-conflict",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-currency", 10m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-auth-currency", 10m, "EUR")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "declined-request-repeat",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-repeat", 1001m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-repeat", 1001m, "USD")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "declines-do-not-reserve-key",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-then-approve", 1001m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-then-approve", 1500m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-then-approve", 1000m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-decline-then-approve", 1000m, "USD")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "validation-errors",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("   ", 10m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-invalid-amount", 0m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-invalid-currency", 10m, "usd")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "boundary-1000-vs-1000-01",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-boundary-ok", 1000m, "USD")),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-boundary-decline", 1000.01m, "USD")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "arbitrary-uppercase-currency-authorizes",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-zzz", 10m, "ZZZ")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "get-missing-payment",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        new PaymentByIdCall(new PaymentPath("not-a-real-id")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "capture-missing-payment",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "CapturePayment",
                        new PaymentByIdCall(new PaymentPath("not-a-real-id")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "void-missing-payment",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "VoidPayment",
                        new PaymentByIdCall(new PaymentPath("not-a-real-id")),
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "capture-lifecycle-and-authorize-replay",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-capture-flow", 25m, "USD")),
                        ContractJson.SerializerOptions);

                    var payment = ResponseReaders.ReadPayment(created.Body);
                    var byId = new PaymentByIdCall(new PaymentPath(payment.Id));
                    var sameAuthorize = new AuthorizePaymentCall(
                        new AuthorizePaymentBody(payment.IdempotencyKey, payment.Amount, payment.Currency));

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "CapturePayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "CapturePayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        sameAuthorize,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody(payment.IdempotencyKey, payment.Amount + 1m, payment.Currency)),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "VoidPayment",
                        byId,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "void-lifecycle-and-authorize-replay",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-void-flow", 30m, "USD")),
                        ContractJson.SerializerOptions);

                    var payment = ResponseReaders.ReadPayment(created.Body);
                    var byId = new PaymentByIdCall(new PaymentPath(payment.Id));
                    var sameAuthorize = new AuthorizePaymentCall(
                        new AuthorizePaymentBody(payment.IdempotencyKey, payment.Amount, payment.Currency));

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "VoidPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "VoidPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        sameAuthorize,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody(payment.IdempotencyKey, payment.Amount + 1m, payment.Currency)),
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "CapturePayment",
                        byId,
                        ContractJson.SerializerOptions);
                }),

            await RecordScenarioAsync(
                "capture-then-get-current-state",
                httpClient,
                target,
                tracesDirectory,
                async session =>
                {
                    var created = await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody("pp-get-captured", 12m, "USD")),
                        ContractJson.SerializerOptions);

                    var payment = ResponseReaders.ReadPayment(created.Body);
                    var byId = new PaymentByIdCall(new PaymentPath(payment.Id));

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "CapturePayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<PaymentByIdCall, ApiCallResponse>(
                        "GetPayment",
                        byId,
                        ContractJson.SerializerOptions);

                    await session.ExecuteAsync<AuthorizePaymentCall, ApiCallResponse>(
                        "AuthorizePayment",
                        new AuthorizePaymentCall(new AuthorizePaymentBody(payment.IdempotencyKey, payment.Amount, payment.Currency)),
                        ContractJson.SerializerOptions);
                }),
        };

        var spec = PaymentProcessingSpec.Create();
        var replaySummaries = new List<object>();

        using (Understanding.UseStrictness(UnderstandingStrictness.Reject))
        {
            foreach (var scenario in scenarios)
            {
                var replay = TraceReplayer.Replay(
                    spec,
                    new PaymentProcessingState(),
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

        Console.WriteLine($"Recorded {scenarios.Count} traces in '{tracesDirectory}'.");
        Console.WriteLine($"Wrote replay summary to '{summaryPath}'.");
    }

    private static async Task EnsureHealthyAsync(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(ContractJson.SerializerOptions);
        if (!string.Equals(payload?.Status, "ok", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PaymentProcessing health probe did not return status 'ok'.");
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
            "PaymentProcessing",
            "PaymentProcessing.Api",
            "openapi.json"));
}
