// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

/// <summary>
/// The payment worker authored as an <b>experimental</b> replay coroutine. The
/// worker's logic is genuinely sequential — read the outbox, take the row, call
/// the gateway, commit the answer, loop — and this is what that logic looks like
/// when it is written the way the implementation is written.
///
/// <para><b>Status: experimental, and deliberately used only in a closed
/// sub-model.</b> <c>Microsoft.Accordant.ModelChecking.Experimental.Coroutines</c>
/// is unpackaged, carries no compatibility promise, and its runtime cannot
/// enforce several of its own rules. See
/// <c>docs/concepts/model-checking-frontends.md</c> and this sample's README for
/// the exact boundary, and <see cref="CompiledStepIsNotAFunctionOfTheStateItIsAppliedTo"/>
/// for the executable reason it is not composed with the controller.</para>
/// </summary>
public static class PaymentWorkerCoroutine
{
    /// <summary>The stable workflow name used in identities and diagnostics.</summary>
    public const string WorkflowName = "payment-worker";

    /// <summary>
    /// Compiles the worker coroutine to an ordinary state graph, starting from
    /// the database a correct controller leaves behind.
    ///
    /// <para>The depth bound is explicit and the caller is expected to confirm
    /// there is no depth frontier: <c>CoroutineModel.Explore</c> defaults to 16
    /// so that a missing <c>Loop</c> boundary fails conservatively, and a
    /// frontier would make every verdict bounded rather than definitive.</para>
    /// </summary>
    public static StateGraphNode BuildGraph(
        FulfillmentConfig config,
        int order = 0,
        int worker = 0)
        => Explore(
            RequireLeasedConfig(config),
            order,
            Workflow(worker, order, config.IdempotentCharge),
            verifyDeterminism: false);

    /// <summary>
    /// The same graph with the opt-in replay-determinism audit enabled.
    ///
    /// <para>The workflow delegate is deliberately explored once before the
    /// audited run. The C# compiler caches the workflow's non-escaping lambdas
    /// in <c>&lt;&gt;9__</c> fields on the closure it captured
    /// <c>worker</c>/<c>order</c> into, and those fields are written the first
    /// time each lambda is evaluated. The audit compares captured variables
    /// around the body, so on a <em>cold</em> delegate it reports the compiler's
    /// own cache as a captured-variable write — see
    /// <c>AuditingAColdDelegateReportsTheCompilersLambdaCache</c> in the tests.
    /// Warming the same delegate instance first is what makes the audit
    /// usable.</para>
    /// </summary>
    public static StateGraphNode BuildAuditedGraph(
        FulfillmentConfig config,
        int order = 0,
        int worker = 0)
    {
        var process = Workflow(worker, order, RequireLeasedConfig(config).IdempotentCharge);
        Explore(config, order, process, verifyDeterminism: false);
        return Explore(config, order, process, verifyDeterminism: true);
    }

    private static StateGraphNode Explore(
        FulfillmentConfig config,
        int order,
        Func<ModelContext<StoreState>, ModelTask> process,
        bool verifyDeterminism)
        => CoroutineModel.Explore(
            WorkflowName,
            StoreState.AfterAcceptedSubmit(config, order),
            process,
            maxDepth: 64,
            verifyDeterminism: verifyDeterminism);

    private static FulfillmentConfig RequireLeasedConfig(FulfillmentConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.LeaseOutboxRows)
        {
            throw new ArgumentException(
                "The coroutine study models one worker on a leased outbox row. " +
                "Duplicate delivery needs two independently active workers, which " +
                "this frontend cannot compose.",
                nameof(config));
        }

        return config;
    }

    /// <summary>
    /// The worker workflow. Only immutable scalars are captured, so
    /// <c>CoroutineModel.DescribeCapturedInputs</c> can monitor every captured
    /// input by value rather than by reference identity.
    /// </summary>
    public static Func<ModelContext<StoreState>, ModelTask> Workflow(
        int worker,
        int order,
        bool idempotentCharge)
        => async context =>
        {
            while (true)
            {
                // The trusted iteration boundary. It is internal: it creates no
                // graph edge, it rebases the replay tape, and it is what makes
                // the retry loop a finite cycle instead of an ever-growing tape.
                await context.Loop("attempt");

                var phase = await context.Read("outbox-row", store => store.Outbox[order]);
                if (phase == OutboxPhase.Absent)
                {
                    return;
                }

                if (phase == OutboxPhase.Pending)
                {
                    await context.Step(
                        FulfillmentAction.PickUp,
                        store => store.PickUpOutboxRow(order, worker, lease: true));
                    continue;
                }

                // The external system's nondeterminism, as a visible but
                // state-neutral control branch.
                var outcome = await context.Choose(
                    "gateway",
                    CallGatewayStep.GatewayOutcomes);

                await context.Step(
                    FulfillmentAction.CallGateway,
                    store => store.RecordGatewayCall(worker, outcome, idempotentCharge));

                await context.Step(
                    FulfillmentAction.Settle,
                    store => store.SettleAttempt(worker));
            }
        };

    /// <summary>
    /// Recovers the shared action label from a compiled coroutine edge, or
    /// <c>null</c> for the state-neutral <c>Choose</c> control edge, which the
    /// domain comparison hides.
    /// </summary>
    public static FulfillmentAction Label(StateGraphEdge edge, int worker, int order)
    {
        if (edge == null) throw new ArgumentNullException(nameof(edge));
        if (!(edge.Metadata is CoroutineTransition transition))
        {
            throw new InvalidOperationException(
                "The compiled coroutine graph must retain CoroutineTransition metadata.");
        }

        return Label(transition, worker, order);
    }

    /// <summary>
    /// Recovers the shared action label from compiled coroutine edge metadata.
    /// </summary>
    public static FulfillmentAction Label(
        CoroutineTransition transition,
        int worker,
        int order)
    {
        if (transition == null) throw new ArgumentNullException(nameof(transition));

        if (transition.Kind == ModelCheckpointKind.Choose)
        {
            return null;
        }

        return transition.CheckpointName switch
        {
            FulfillmentAction.PickUp =>
                new FulfillmentAction(FulfillmentAction.PickUp, order, worker),
            FulfillmentAction.CallGateway =>
                new FulfillmentAction(
                    FulfillmentAction.CallGateway,
                    order,
                    worker,
                    Gateway: SelectedOutcome(transition)),
            FulfillmentAction.Settle =>
                new FulfillmentAction(FulfillmentAction.Settle, order, worker),
            _ => throw new InvalidOperationException(
                $"Unexpected coroutine checkpoint '{transition.CheckpointName}'.")
        };
    }

    /// <summary>
    /// Maps a compiled coroutine edge to the hand-written worker action it
    /// stands for. <c>Choose</c> is declared hidden — the checked claim that the
    /// hand-written model does not move when the coroutine picks a gateway
    /// outcome.
    /// </summary>
    public static AbstractResponse MapToManualWorker(
        RefinementTransition<StoreState> transition,
        int worker,
        int order)
        => MapToManualWorker(transition.Metadata, worker, order);

    /// <summary>
    /// The same mapping expressed over raw compiled edge metadata, so the
    /// declaration can also be inspected directly on the graph.
    /// </summary>
    public static AbstractResponse MapToManualWorker(
        object metadata,
        int worker,
        int order)
    {
        if (!(metadata is CoroutineTransition coroutine))
        {
            throw new InvalidOperationException(
                "The compiled coroutine graph must retain CoroutineTransition metadata.");
        }

        if (coroutine.Kind == ModelCheckpointKind.Choose)
        {
            return AbstractResponse.Hidden;
        }

        var expected = Label(coroutine, worker, order);
        return AbstractResponse.Matching(
            candidate => !candidate.IsStutter && Equals(candidate.Metadata, expected));
    }

    /// <summary>
    /// The gateway outcome an edge stands for: the value the <c>Choose</c> edge
    /// itself selected, or the value the replay prefix of a later <c>Step</c>
    /// edge recorded. Worker locals come from typed replay metadata, never from
    /// parsing a step-function identity.
    /// </summary>
    public static GatewayOutcome SelectedOutcome(CoroutineTransition transition)
    {
        if (transition == null) throw new ArgumentNullException(nameof(transition));

        if (transition.Kind == ModelCheckpointKind.Choose &&
            transition.Value is GatewayOutcome selected)
        {
            return selected;
        }

        var recorded = transition.ReplayPrefix.LastOrDefault(entry =>
            entry.Kind == ModelCheckpointKind.Choose && entry.Name == "gateway");
        if (recorded?.Value is GatewayOutcome outcome)
        {
            return outcome;
        }

        throw new InvalidOperationException(
            $"Coroutine transition '{transition}' has no gateway replay selection.");
    }

    /// <summary>
    /// The gateway outcomes still possible for an order, derived from state.
    /// Used only by the composition-boundary study below.
    /// </summary>
    public static IReadOnlyList<GatewayOutcome> OutcomesStillPossible(
        StoreState store,
        int order)
        => store.Charges[order] == 0
            ? CallGatewayStep.GatewayOutcomes
            : new[] { GatewayOutcome.Charged };

    /// <summary>
    /// The executable reason this frontend is not composed with the controller
    /// or with a second worker.
    ///
    /// <para>A compiled coroutine step carries a <em>pending</em> checkpoint that
    /// was computed by advancing the workflow from the state at which the
    /// coroutine arrived — the previous visible edge's target. Its internal
    /// <c>Read</c> values and, for a state-derived <c>Choose</c>, its materialized
    /// choice set are therefore fused into the <em>preceding</em> transition. A
    /// hand-written step is a function of the state it is applied to; a compiled
    /// coroutine step is not.</para>
    ///
    /// <para>This method takes a compiled step out of a graph built from one
    /// state and applies it to another, which is exactly what interleaving with
    /// an independently active step would do, and returns the choices it offers.
    /// The stale answer is the defect: the model would explore a branch the
    /// workflow's own selector excludes in that state, so mixing the frontends
    /// could produce a fabricated counterexample or, symmetrically, hide a real
    /// interleaving behind a false <c>Holds</c>.</para>
    /// </summary>
    public static (
        IReadOnlyList<GatewayOutcome> OfferedAfterInterference,
        IReadOnlyList<GatewayOutcome> SelectorWouldAllow)
        CompiledStepIsNotAFunctionOfTheStateItIsAppliedTo()
    {
        const int order = 0;
        var config = FulfillmentConfig.SingleWorker;

        var uncharged = StoreState.AfterAcceptedSubmit(config, order);
        uncharged.PickUpOutboxRow(order, 0, lease: true);

        var root = CoroutineModel.Explore(
            "stale-choice-study",
            uncharged,
            StaleChoiceWorkflow(order),
            maxDepth: 4);
        var compiled = (ICoroutineCheckpointStep)root.StepFunctions.Single();

        // Another process charged the order between the coroutine reaching its
        // Choose and the Choose being taken.
        var chargedInTheMeantime = (StoreState)root.State.Clone();
        chargedInTheMeantime.Charges[order] = 1;

        var offered = compiled
            .Apply(chargedInTheMeantime, Array.Empty<(IStepFunction, StateGraphNode)>())
            .Select(result => SelectedOutcome((CoroutineTransition)result.EdgeMetadata))
            .ToArray();

        return (offered, OutcomesStillPossible(chargedInTheMeantime, order));
    }

    private static Func<ModelContext<StoreState>, ModelTask> StaleChoiceWorkflow(int order)
        => async context =>
        {
            var outcome = await context.Choose(
                "gateway",
                store => OutcomesStillPossible(store, order));
            await context.Step(
                "record",
                store => store.RecordGatewayCall(0, outcome, idempotent: true));
        };
}
