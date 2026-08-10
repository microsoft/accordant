namespace Microsoft.Accordant.ModelChecking.Operations;

/// <summary>
/// Metadata on a model edge produced by an Accordant operation.
/// </summary>
public sealed class OperationModelTransition
{
    internal OperationModelTransition(
        string operationName,
        IOperation operation,
        object request,
        object response)
    {
        OperationName = operationName;
        Operation = operation;
        Request = request;
        Response = response;
    }

    /// <summary>The stable name of the bound operation input.</summary>
    public string OperationName { get; }

    /// <summary>The operation whose model was invoked.</summary>
    public IOperation Operation { get; }

    /// <summary>The request supplied to the operation.</summary>
    public object Request { get; }

    /// <summary>The mock response selecting this modeled outcome.</summary>
    public object Response { get; }

    /// <inheritdoc/>
    public override string ToString()
        => Response == null
            ? OperationName
            : $"{OperationName} -> {Response}";
}
