// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace OrderFulfillment;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Operations;

/// <summary>The checkout request: one order id.</summary>
public sealed class SubmitOrderRequest
{
    /// <summary>Creates an immutable request.</summary>
    public SubmitOrderRequest(int order)
    {
        Order = order;
    }

    /// <summary>The order id being submitted.</summary>
    public int Order { get; }

    /// <inheritdoc/>
    public override string ToString() => $"SubmitOrder(o{Order})";
}

/// <summary>
/// The checkout controller written as an Accordant <see cref="Operation{TRequest,
/// TResponse, TState}"/>: the same specification style used for conformance
/// testing, reused unchanged as a model-checking frontend.
///
/// <para>The three responses are genuinely response-dependent state: an accepted
/// submit commits the order row <em>and</em> the outbox row and queues the
/// background handlers, a rejected submit commits only the order row, and a
/// duplicate submit is a real request that writes nothing.</para>
/// </summary>
public sealed class SubmitOrderOperation
    : Operation<SubmitOrderRequest, SubmitOutcome, StoreState>
{
    private readonly FulfillmentConfig config;
    private readonly bool queueWorkers;

    /// <summary>Creates the controller operation for one model instance.</summary>
    public SubmitOrderOperation(
        FulfillmentConfig config,
        bool queueWorkers = true)
        : base("SubmitOrder")
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.queueWorkers = queueWorkers;
    }

    /// <inheritdoc/>
    public override ExpectedOutcomes Apply(SubmitOrderRequest request, StoreState state)
    {
        var order = request.Order;

        if (state.Orders[order] != OrderStatus.Missing)
        {
            // The mock response matters here: it is the response the compiled
            // graph edge carries. An outcome with no mock would be explored as
            // the response type's default value.
            return Expect.That(
                    response => response == SubmitOutcome.Duplicate,
                    "an existing order id is not resubmitted")
                .ThenState(
                    (_, _) => { },
                    mock: () => SubmitOutcome.Duplicate);
        }

        var accepted = Expect.That(
                    response => response == SubmitOutcome.Accepted,
                    "risk screening passed")
                .ThenState(
                    (_, next) => next.CommitAcceptedOrder(order),
                    mock: () => SubmitOutcome.Accepted);
        if (queueWorkers)
        {
            accepted = accepted.Triggers(
                FulfillmentModel.QueuedWorkFor(config, order).ToArray());
        }

        return Expect.OneOf(
            accepted,
            Expect.That(
                    response => response == SubmitOutcome.Rejected,
                    "risk screening refused")
                .ThenState(
                    (_, next) => next.CommitRejectedOrder(order),
                    mock: () => SubmitOutcome.Rejected));
    }
}

/// <summary>
/// Builds the composed graph in which the controller is authored with the
/// <c>Operation</c> frontend and the background workers are hand-written
/// <see cref="IStepFunction"/> implementations.
/// </summary>
public static class OperationsFrontend
{
    /// <summary>
    /// The bound controller inputs. Each one is a finite request; the frontend
    /// turns every mock response into an ordinary graph branch.
    /// </summary>
    public static IReadOnlyList<OperationModelStep> ControllerSteps(
        FulfillmentConfig config,
        bool queueWorkers = true)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var operation = new SubmitOrderOperation(config, queueWorkers);
        return Enumerable.Range(0, config.Orders)
            .Select(order => new OperationModelStep(
                operation.With(
                    new SubmitOrderRequest(order),
                    SubmitOrderStep.IdFor(order)),
                repeat: true))
            .ToArray();
    }

    /// <summary>
    /// The composed graph. The controller's queued work arrives through the
    /// operation's <c>Triggers</c> clause, so the worker handlers for an order
    /// id exist in the graph only after the accepting transaction committed.
    /// </summary>
    public static StateGraphNode BuildGraph(FulfillmentConfig config)
        => OperationModel.Explore(
            StoreState.Empty(config),
            ControllerSteps(config));

    /// <summary>
    /// The composed graph built the other way round: the worker handlers are
    /// active from the start, exactly as in the hand-written model, and only the
    /// controller is authored as an operation. This is the shape that shows the
    /// two frontends interleaving in one graph rather than one queueing the
    /// other.
    /// </summary>
    public static StateGraphNode BuildCoActiveGraph(FulfillmentConfig config)
        => OperationModel.Explore(
            StoreState.Empty(config),
            ControllerSteps(config, queueWorkers: false),
            additionalSteps: FulfillmentModel.WorkerSteps(config));

    /// <summary>
    /// Recovers the shared action label from a composed edge. Operation edges
    /// carry <see cref="OperationModelTransition"/> metadata with the actual
    /// response; hand-written worker edges already carry a
    /// <see cref="FulfillmentAction"/>.
    /// </summary>
    public static FulfillmentAction Label(StateGraphEdge edge)
    {
        if (edge == null) throw new ArgumentNullException(nameof(edge));

        if (edge.Metadata is FulfillmentAction action)
        {
            return action;
        }

        if (edge.Metadata is OperationModelTransition transition)
        {
            var request = (SubmitOrderRequest)transition.Request;
            return new FulfillmentAction(
                FulfillmentAction.Submit,
                request.Order,
                StoreState.NoWorker,
                (SubmitOutcome)transition.Response);
        }

        throw new InvalidOperationException(
            $"Unexpected composed edge metadata '{edge.Metadata}'.");
    }
}
