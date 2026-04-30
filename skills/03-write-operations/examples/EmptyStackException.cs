// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// Custom exception for empty stack operations.
/// Operations can use Expect.Throws<TException>() to model error cases.
/// </summary>
public class EmptyStackException : Exception
{
    public EmptyStackException() : base("Stack is empty") { }
    public EmptyStackException(string message) : base(message) { }
}
