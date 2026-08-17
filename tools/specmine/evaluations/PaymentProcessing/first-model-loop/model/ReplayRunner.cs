using System.Text;
using System.Text.Json;
using Microsoft.Accordant;
using Specmine;
using Specmine.Accordant;

namespace PaymentModel;

/// <summary>How one recorded call was treated by this model revision.</summary>
public enum StepClassification
{
    Conforming,
    ModelViolation,
    NotApplicableUnknown,
}

/// <param name="Region">
/// Which modeled claim checked the step, or which unknown region excluded it. Rendered in
/// the report so "conforming" and "excluded" are always attributable to a named region.
/// </param>
public sealed record StepReport(int CallId, string OperationName, StepClassification Classification, string Region, string Detail);

public sealed class TraceReport
{
    public required string TracePath { get; init; }
    public required Guid TraceId { get; init; }
    public List<StepReport> Steps { get; } = new();

    /// <summary>
    /// Trace-level verdict. A trace is only ever called "Conforming" when every step in it
    /// was actually checked against the model and passed. If a real model violation
    /// occurred anywhere, the trace is a violation. Otherwise, if any step was excluded as
    /// outside modeled scope, the trace is reported as "conforming (partial)" -- or, when
    /// no substantive step was checked at all, as "not applicable/unknown" rather than
    /// conforming. See the per-step detail for exactly what was and was not verified.
    /// </summary>
    public string OverallVerdict()
    {
        if (Steps.Any(s => s.Classification == StepClassification.ModelViolation))
        {
            return "MODEL VIOLATION";
        }

        if (Steps.Any(s => s.Classification == StepClassification.NotApplicableUnknown))
        {
            // Reset is scaffolding that establishes initial state for every trace; it does
            // not, by itself, demonstrate anything about the substantive claims this model
            // makes. Only count conforming non-Reset steps towards a "conforming" verdict,
            // so a trace whose only substantive calls are excluded (e.g. the all-decline
            // trace 3) is reported as "not applicable/unknown", not "conforming".
            var checkedCount = Steps.Count(s =>
                s.Classification == StepClassification.Conforming && s.OperationName != PaymentSpec.ResetOperation);
            var excludedCount = Steps.Count(s => s.Classification == StepClassification.NotApplicableUnknown);
            return checkedCount > 0
                ? $"CONFORMING (partial) -- {checkedCount} substantive step(s) checked and conforming, {excludedCount} step(s) intentionally excluded (unknown region)"
                : $"NOT APPLICABLE / UNKNOWN -- all {excludedCount} substantive step(s) are outside this model revision's scope";
        }

        return "CONFORMING";
    }
}

/// <summary>
/// Replays every recorded trace under the workspace's traces\ directory through the
/// partial M-002 model (<see cref="PaymentSpec"/>).
///
/// Each recorded call is classified by <see cref="ModelScope"/> -- the same classifier the
/// spec's Apply uses to decide where its claims are defined -- and then either checked
/// against the model through Specmine.Accordant's TraceReplayer/spec.Allows, or excluded
/// as an explicitly unknown region and reported as such. Calls are replayed one at a time
/// rather than in consecutive batches, for two reasons: the model state (including
/// lifecycle status, which CapturePayment mutates) is then always current when the next
/// call is classified, and every call gets its own verdict instead of being silently
/// dropped when an earlier call in the same batch fails.
/// </summary>
public static class ReplayRunner
{
    public static async Task<int> Main(string[] args)
    {
        var workspaceRoot = args.Length > 0 ? args[0] : FindWorkspaceRoot();
        var tracesDir = Path.Combine(workspaceRoot, "traces");
        if (!Directory.Exists(tracesDir))
        {
            Console.Error.WriteLine($"Traces directory not found: {tracesDir}");
            return 1;
        }

        var tracePaths = Directory.GetFiles(tracesDir, "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (tracePaths.Count == 0)
        {
            Console.Error.WriteLine($"No trace files found under {tracesDir}");
            return 1;
        }

        var jsonOptions = ReplayJsonOptions.CreateDefault();
        var spec = PaymentSpec.Build();

        var reports = new List<TraceReport>();
        foreach (var tracePath in tracePaths)
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, tracePath);
            reports.Add(await ReplayTraceAsync(spec, tracePath, relativePath, jsonOptions));
        }

        var reportMarkdown = RenderReport(reports);
        Console.Write(reportMarkdown);

        var reportPath = Path.Combine(FindModelDir(workspaceRoot), "replay-report.md");
        await File.WriteAllTextAsync(reportPath, reportMarkdown);
        Console.Error.WriteLine($"\nWrote {reportPath}");

        var anyViolation = reports.Any(r => r.Steps.Any(s => s.Classification == StepClassification.ModelViolation));
        return anyViolation ? 2 : 0;
    }

    private static async Task<TraceReport> ReplayTraceAsync(Spec<PaymentModelState> spec, string tracePath, string relativePath, JsonSerializerOptions jsonOptions)
    {
        var trace = await TraceStore.LoadAsync(tracePath);
        var report = new TraceReport { TracePath = relativePath, TraceId = trace.TraceId };

        PaymentModelState currentState = new();

        // Checks one recorded call against the model and advances the model state. Only
        // ever called for calls ModelScope classifies as inside the modeled region.
        void Check(RecordedCall call, string region)
        {
            var subTrace = new RecordedTrace(
                trace.SchemaVersion,
                trace.TraceId,
                trace.StartedAt,
                trace.CompletedAt,
                trace.Status,
                new[] { call });

            var result = TraceReplayer.Replay(spec, currentState, subTrace, jsonOptions);
            foreach (var step in result.Steps)
            {
                var conforming = step.Outcome == ReplayStepOutcome.Conforming;
                var detail = conforming
                    ? $"Checked against the model via spec.Allows/TraceReplayer under {region}; the observed response was allowed."
                    : $"Checked against the model via spec.Allows/TraceReplayer under {region} and FAILED ({step.Outcome}): {step.Message}";
                report.Steps.Add(new StepReport(
                    step.CallId,
                    step.OperationName,
                    conforming ? StepClassification.Conforming : StepClassification.ModelViolation,
                    region,
                    detail));
            }

            // On a conforming step this spec always resolves to exactly one next state (it
            // never produces nondeterministic branches). If the step violated the model,
            // FinalStateProfile may be unavailable -- keep the last known-good state so the
            // remaining calls in the trace still get a verdict.
            if (result.FinalStateProfile is { } profile)
            {
                var nextState = profile.SingleState()
                    ?? throw new InvalidOperationException(
                        "Expected a single deterministic next state after replaying a call, but the state " +
                        "profile did not resolve to exactly one state.");
                currentState = (PaymentModelState)nextState;
            }
        }

        void Exclude(RecordedCall call, string region, string detail) =>
            report.Steps.Add(new StepReport(call.CallId, call.OperationName, StepClassification.NotApplicableUnknown, region, detail));

        foreach (var call in trace.Calls)
        {
            if (call.Response is null)
            {
                var errorDetail = call.Error is null ? "no response and no error recorded" : call.Error.Message;
                Exclude(call, "Unknown region: no observed response",
                    $"The recorded call has no observed response to check or to learn from ({errorDetail}). " +
                    "Excluded; model state unchanged.");
                continue;
            }

            switch (call.OperationName)
            {
                case PaymentSpec.ResetOperation:
                    // Always in scope: Reset transitions from ANY state to empty state.
                    Check(call, "Reset scaffolding");
                    break;

                case PaymentSpec.AuthorizePaymentOperation:
                    ReplayAuthorize(call);
                    break;

                case PaymentSpec.CapturePaymentOperation:
                    ReplayCapture(call);
                    break;

                default:
                    Exclude(call, $"Unknown region: unmodeled operation '{call.OperationName}'",
                        $"Operation '{call.OperationName}' is outside this model revision's scope (only Reset, " +
                        "AuthorizePayment and CapturePayment are modeled; see model\\README.md). Excluded; model " +
                        "state unchanged.");
                    break;
            }
        }

        return report;

        void ReplayAuthorize(RecordedCall call)
        {
            var requestEnvelope = JsonSerializer.Deserialize<AuthorizePaymentRequestEnvelope>(call.Request.GetRawText(), jsonOptions);
            var scope = ModelScope.ClassifyAuthorize(requestEnvelope?.Body, currentState);

            if (ModelScope.IsModeled(scope))
            {
                Check(call, ModelScope.RegionLabel(scope));
                return;
            }

            var detail = ModelScope.Describe(scope, requestEnvelope?.Body);

            // The one place this model learns instead of judging: a fresh/previously-declined
            // key whose observed response really was a 201. The server-generated id and the
            // payload it recorded are captured verbatim from the response (never invented),
            // together with the initial 'authorized' lifecycle status, so later calls under
            // that key -- and captures of that payment -- become checkable.
            if (scope == AuthorizeScope.UnknownKeyOutcomeSelection)
            {
                var key = requestEnvelope!.Body!.IdempotencyKey!;
                var responseEnvelope = JsonSerializer.Deserialize<AuthorizePaymentResponseEnvelope>(
                    call.Response!.Value.GetRawText(), jsonOptions);

                if (responseEnvelope is { Status: 201 } && responseEnvelope.Body is { Id: { Length: > 0 } id, Amount: { } amount, Currency: { Length: > 0 } currency })
                {
                    currentState = new PaymentModelState
                    {
                        SuccessfulKeys = new Dictionary<string, CapturedAuthorization>(currentState.SuccessfulKeys)
                        {
                            [key] = new CapturedAuthorization
                            {
                                PaymentId = id,
                                IdempotencyKey = key,
                                Amount = amount,
                                Currency = currency,
                            },
                        },
                        LifecycleStatuses = new Dictionary<string, string>(currentState.LifecycleStatuses)
                        {
                            [id] = PaymentLifecycle.Authorized,
                        },
                    };

                    detail += $" The observed response was a real 201 (id={id}, amount={amount}, currency={currency}), " +
                              "so it is captured into model state (identity/data plus an 'authorized' lifecycle status) " +
                              "for downstream replay only; this establishment call itself is not checked against the model.";
                }
                else
                {
                    detail += $" The observed response (HTTP {responseEnvelope?.Status}) was not a capturable success, so " +
                              "model state is unchanged and further calls under this key stay outside the modeled region.";
                }
            }

            Exclude(call, ModelScope.RegionLabel(scope), detail);
        }

        void ReplayCapture(RecordedCall call)
        {
            var requestEnvelope = JsonSerializer.Deserialize<CapturePaymentRequestEnvelope>(call.Request.GetRawText(), jsonOptions);
            var scope = ModelScope.ClassifyCapture(requestEnvelope, currentState);

            if (ModelScope.IsModeled(scope))
            {
                Check(call, ModelScope.RegionLabel(scope));
                return;
            }

            // No bootstrap here: this model only ever learns a payment's identity from a
            // real AuthorizePayment 201, so an unmodeled capture leaves state untouched.
            Exclude(call, ModelScope.RegionLabel(scope),
                ModelScope.Describe(scope, requestEnvelope) + " Excluded; model state unchanged.");
        }
    }

    private static string RenderReport(IReadOnlyList<TraceReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Replay Report -- model M-002");
        sb.AppendLine();
        sb.AppendLine("Generated by `model\\ReplayRunner.cs`. Reproduce with:");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("cd model");
        sb.AppendLine("dotnet build");
        sb.AppendLine("dotnet run --no-build");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Every recorded trace under `traces\\` is replayed, one call at a time, in trace-file order. " +
                       "Each call is classified as one of:");
        sb.AppendLine();
        sb.AppendLine("- **Conforming** -- checked against the model via `spec.Allows`/`TraceReplayer`, and the observed response was allowed.");
        sb.AppendLine("- **Model violation** -- checked against the model, and the observed response was *not* allowed.");
        sb.AppendLine("- **Not applicable / unknown** -- intentionally excluded from model acceptance because it falls outside " +
                       "M-002's modeled region. The exact region is named for every excluded step, and summarized at the end.");
        sb.AppendLine();
        sb.AppendLine("No trace or step is ever reported as conforming unless it was actually checked against the model and passed. " +
                       "Because calls are replayed individually rather than in batches, a violation never suppresses the verdict of a later call.");
        sb.AppendLine();

        int totalConforming = 0, totalViolations = 0, totalExcluded = 0;
        var conformingByRegion = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var excludedByRegion = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var report in reports)
        {
            sb.AppendLine($"## `{report.TracePath}`");
            sb.AppendLine();
            sb.AppendLine($"Trace id: `{report.TraceId}`  ");
            sb.AppendLine($"Verdict: **{report.OverallVerdict()}**");
            sb.AppendLine();
            sb.AppendLine("| Call | Operation | Classification | Region | Detail |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var step in report.Steps)
            {
                var label = step.Classification switch
                {
                    StepClassification.Conforming => "Conforming",
                    StepClassification.ModelViolation => "MODEL VIOLATION",
                    StepClassification.NotApplicableUnknown => "Not applicable / unknown",
                    _ => step.Classification.ToString(),
                };
                var region = step.Region.Replace("|", "\\|");
                var detail = step.Detail.Replace("|", "\\|");
                sb.AppendLine($"| {step.CallId} | {step.OperationName} | {label} | {region} | {detail} |");

                switch (step.Classification)
                {
                    case StepClassification.Conforming:
                        totalConforming++;
                        conformingByRegion[step.Region] = conformingByRegion.GetValueOrDefault(step.Region) + 1;
                        break;
                    case StepClassification.ModelViolation:
                        totalViolations++;
                        break;
                    case StepClassification.NotApplicableUnknown:
                        totalExcluded++;
                        excludedByRegion[step.Region] = excludedByRegion.GetValueOrDefault(step.Region) + 1;
                        break;
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Checked steps by modeled claim");
        sb.AppendLine();
        sb.AppendLine("| Modeled region | Steps checked and conforming |");
        sb.AppendLine("|---|---|");
        foreach (var (region, count) in conformingByRegion)
        {
            sb.AppendLine($"| {region.Replace("|", "\\|")} | {count} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Excluded steps by unknown region");
        sb.AppendLine();
        if (excludedByRegion.Count == 0)
        {
            sb.AppendLine("None -- every recorded call fell inside the modeled region.");
        }
        else
        {
            sb.AppendLine("| Unknown region | Steps excluded |");
            sb.AppendLine("|---|---|");
            foreach (var (region, count) in excludedByRegion)
            {
                sb.AppendLine($"| {region.Replace("|", "\\|")} | {count} |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Traces replayed: {reports.Count}");
        sb.AppendLine($"- Steps recorded in those traces: {totalConforming + totalViolations + totalExcluded}");
        sb.AppendLine($"- Steps conforming: {totalConforming}");
        sb.AppendLine($"- Steps in model violation: {totalViolations}");
        sb.AppendLine($"- Steps not applicable / unknown (excluded from acceptance): {totalExcluded}");
        sb.AppendLine();
        sb.AppendLine(totalViolations == 0
            ? "All applicable, checked evidence conforms to the model. Nothing above was accepted without being checked; " +
              "see model\\README.md for what remains unknown and how to attack it."
            : "One or more checked steps violated the model -- see the table(s) above.");

        return sb.ToString();
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "workspace.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Fallback: assume we're running from the model project directory (the common
        // `dotnet run` case), so the workspace root is the parent directory.
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
    }

    private static string FindModelDir(string workspaceRoot) => Path.Combine(workspaceRoot, "model");
}
