namespace Microsoft.Accordant.ModelChecking;

using System;
using System.Globalization;

internal sealed class AugmentationRuntime<TConcrete, TAuxiliary>
    : IAugmentationRuntime
    where TConcrete : IState
    where TAuxiliary : State
{
    private readonly Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next;
    private readonly FrozenStateRegistry registry =
        new FrozenStateRegistry(
            value => $"augmentation state of type '{value.GetType().Name}'",
            "The augmentation state was not initialized by this check.");

    public AugmentationRuntime(
        StateGraphNode concreteRoot,
        Func<TConcrete, TAuxiliary> initial,
        Func<TAuxiliary, RefinementTransition<TConcrete>, TAuxiliary> next)
    {
        this.next = next;
        InitialState = Prepare(
            initial(GetConcrete(concreteRoot)),
            "The augmentation initializer returned null.");
    }

    public object InitialState { get; }

    public object Advance(
        object auxiliaryState,
        StateGraphNode source,
        StateGraphEdge edge)
    {
        var current = (TAuxiliary)auxiliaryState;
        var updated = next(
            current,
            new RefinementTransition<TConcrete>(
                GetConcrete(source),
                edge.StepFunction,
                edge.Metadata,
                GetConcrete(edge.Target)));
        Validate(current);
        return Prepare(
            updated,
            "The augmentation update returned null.");
    }

    public string GetIdentity(object auxiliaryState)
    {
        var value = (TAuxiliary)auxiliaryState;
        Validate(value);
        return value
            .GetStateHash()
            .ToString("X16", CultureInfo.InvariantCulture);
    }

    public TAuxiliary GetValue(object auxiliaryState)
        => (TAuxiliary)auxiliaryState;

    public void Validate(object auxiliaryState)
        => registry.Validate((TAuxiliary)auxiliaryState);

    private TAuxiliary Prepare(TAuxiliary value, string nullMessage)
    {
        if (ReferenceEquals(value, null))
        {
            throw new InvalidOperationException(nullMessage);
        }
        registry.Prepare(value);
        return value;
    }

    private static TConcrete GetConcrete(StateGraphNode node)
    {
        if (node.State is TConcrete concrete)
        {
            return concrete;
        }

        throw new InvalidOperationException(
            $"Concrete graph node state '{node.State?.GetType().FullName}' " +
            $"is not a {typeof(TConcrete).FullName}.");
    }
}
