// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using Microsoft.Accordant;
/// <summary>
/// Spec for a Stack of integers.
/// The Spec class:
/// - Declares all operations as public properties
/// - Calls RegisterOperationProperties() in the constructor
/// 
/// This is the canonical pattern for class-based specs.
/// </summary>
public class StackSpec : Spec<StackState<int>>
{
    // Declare operations as public properties - they will be auto-registered
    public PushOperation<int> Push { get; } = new();
    public PopOperation<int> Pop { get; } = new();
    public PeekOperation<int> Peek { get; } = new();
    public CountOperation<int> Count { get; } = new();
    public IsEmptyOperation<int> IsEmpty { get; } = new();

    public StackSpec()
    {
        // Auto-register all public IOperation properties
        // This discovers Push, Pop, Peek, Count, IsEmpty
        RegisterOperationProperties();
    }
}
