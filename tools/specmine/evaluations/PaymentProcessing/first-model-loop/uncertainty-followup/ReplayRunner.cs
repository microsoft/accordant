using System.Text;
using System.Text.Json;
using Microsoft.Accordant;
using PaymentModel;
using Specmine;
using Specmine.Accordant;

namespace PaymentResearchModel;

public static class ReplayRunner
{
    public static async Task Main()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var traces = Directory.GetFiles(Path.Combine(root, "traces"), "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "uncertainty-followup", "adversary", "traces"), "*.json", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var options = ReplayJsonOptions.CreateDefault();
        var spec = PaymentResearchSpec.Build();
        var counts = Enum.GetValues<ReplayStepOutcome>().ToDictionary(outcome => outcome, _ => 0);
        var details = new List<string>();

        foreach (var path in traces)
        {
            var trace = await TraceStore.LoadAsync(path);
            var state = new PaymentModelState();
            var stateReliable = true;

            foreach (var call in trace.Calls)
            {
                if (!stateReliable && call.OperationName != PaymentResearchSpec.ResetOperation)
                {
                    if (TryRecoverSuccessfulAuthorization(call, state, options, out var recoveredFromUnreliable))
                    {
                        state = recoveredFromUnreliable;
                        stateReliable = true;
                        counts[ReplayStepOutcome.Unknown]++;
                        details.Add($"- `{Path.GetFileName(path)}` call {call.CallId} `{call.OperationName}`: **Unknown** — prior unknown state; recovered successful authorization for downstream replay.");
                    }
                    else
                    {
                        counts[ReplayStepOutcome.Unknown]++;
                        details.Add($"- `{Path.GetFileName(path)}` call {call.CallId} `{call.OperationName}`: **Unknown** — replay skipped because a prior unknown call left model state unreliable.");
                    }

                    continue;
                }

                var singleCallTrace = new RecordedTrace(
                    trace.SchemaVersion,
                    trace.TraceId,
                    trace.StartedAt,
                    trace.CompletedAt,
                    trace.Status,
                    [call]);
                var result = TraceReplayer.Replay(spec, state, singleCallTrace, options);
                var step = result.Steps.Single();
                counts[step.Outcome]++;
                details.Add(
                    $"- `{Path.GetFileName(path)}` call {call.CallId} `{call.OperationName}`: " +
                    $"**{step.Outcome}**{(step.Message is null ? string.Empty : $" — {step.Message}")}");

                if (step.Outcome is ReplayStepOutcome.Conforming or ReplayStepOutcome.ProvisionalMatch &&
                    result.FinalStateProfile?.SingleState() is PaymentModelState nextState)
                {
                    state = nextState;
                    continue;
                }

                if (step.Outcome == ReplayStepOutcome.Unknown &&
                    (TryRecoverSuccessfulAuthorization(call, state, options, out var recovered) ||
                     TryRecoverLifecycleTransition(call, step, state, options, out recovered)))
                {
                    state = recovered;
                    details.Add("  - Recovered model state from the observed response for downstream replay; the call remains Unknown.");
                    stateReliable = true;
                }
                else if (step.Outcome is not (ReplayStepOutcome.Conforming or ReplayStepOutcome.ProvisionalMatch))
                {
                    stateReliable = false;
                }
            }

        }

        var report = new StringBuilder()
            .AppendLine("# Uncertainty-aware Payment replay")
            .AppendLine()
            .AppendLine($"Replayed {traces.Length} immutable traces call-by-call through the M-002 behavior with explicit research annotations.")
            .AppendLine()
            .AppendLine("| Outcome | Calls |")
            .AppendLine("| --- | ---: |");

        foreach (var outcome in Enum.GetValues<ReplayStepOutcome>())
        {
            report.AppendLine($"| {outcome} | {counts[outcome]} |");
        }

        report.AppendLine().AppendLine("## Calls").AppendLine();
        foreach (var detail in details)
        {
            report.AppendLine(detail);
        }

        var reportPath = Path.Combine(root, "uncertainty-followup", "replay-report.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());
        Console.Write(report);
    }

    private static bool TryRecoverLifecycleTransition(
        RecordedCall call,
        ReplayStepResult step,
        PaymentModelState state,
        JsonSerializerOptions options,
        out PaymentModelState recovered)
    {
        recovered = state;
        if (step.ResearchId != "void-lifecycle-transition" || call.Response is not { } responseJson)
        {
            return false;
        }

        var response = JsonSerializer.Deserialize<CapturePaymentResponseEnvelope>(responseJson, options);
        if (response is not { Status: 200, Body: { Id: { Length: > 0 } id, Status: { Length: > 0 } status } } ||
            ModelScope.TryFindPayment(state, id) is null)
        {
            return false;
        }

        recovered = new PaymentModelState
        {
            SuccessfulKeys = new Dictionary<string, CapturedAuthorization>(state.SuccessfulKeys),
            LifecycleStatuses = new Dictionary<string, string>(state.LifecycleStatuses)
            {
                [id] = status,
            },
        };
        return true;
    }

    private static bool TryRecoverSuccessfulAuthorization(
        RecordedCall call,
        PaymentModelState state,
        JsonSerializerOptions options,
        out PaymentModelState recovered)
    {
        recovered = state;
        if (call.OperationName != PaymentResearchSpec.AuthorizePaymentOperation ||
            call.Response is not { } responseJson)
        {
            return false;
        }

        var request = JsonSerializer.Deserialize<AuthorizePaymentRequestEnvelope>(call.Request, options);
        var response = JsonSerializer.Deserialize<AuthorizePaymentResponseEnvelope>(responseJson, options);
        if (request?.Body is not { IdempotencyKey: { Length: > 0 } key } body ||
            response is not { Status: 201, Body: { Id: { Length: > 0 } id, Amount: { } amount, Currency: { Length: > 0 } currency } })
        {
            return false;
        }

        recovered = new PaymentModelState
        {
            SuccessfulKeys = new Dictionary<string, CapturedAuthorization>(state.SuccessfulKeys)
            {
                [key] = new CapturedAuthorization
                {
                    PaymentId = id,
                    IdempotencyKey = key,
                    Amount = amount,
                    Currency = currency,
                },
            },
            LifecycleStatuses = new Dictionary<string, string>(state.LifecycleStatuses)
            {
                [id] = PaymentLifecycle.Authorized,
            },
        };
        return true;
    }
}
