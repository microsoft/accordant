// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Stack
{
    using Microsoft.Accordant;
    /// <summary>
    /// Spec for a Stack of integers.
    /// Contains all operations that can be performed on the stack.
    /// </summary>
    public class StackSpec : Spec<StackState<int>>
    {
        public PushOperation<int> Push { get; } = new();
        public PopOperation<int> Pop { get; } = new();
        public PeekOperation<int> Peek { get; } = new();
        public CountOperation<int> Count { get; } = new();
        public IsEmptyOperation<int> IsEmpty { get; } = new();

        public StackSpec()
        {
            RegisterOperationProperties();
        }
    }
}
