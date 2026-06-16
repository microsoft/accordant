// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Threading.Tasks;

/// <summary>
/// Extension methods for <see cref="IOperation"/>.
/// </summary>
public static class OperationExtensions
{
    /// <summary>
    /// Executes the operation safely, catching any exceptions and returning them as the response.
    /// This ensures that the framework always receives a response (either the actual result or an exception).
    /// </summary>
    /// <param name="operation">The operation to invoke.</param>
    /// <param name="context">The testing context.</param>
    /// <param name="request">The request to execute.</param>
    /// <returns>
    /// The response from execution. If an exception is thrown during execution,
    /// the exception itself is returned as the response.
    /// </returns>
    public static async Task<object> SafeExecuteAsync(
        this IOperation operation,
        TestingContext context,
        object request)
    {
        try
        {
            return await operation.ExecuteAsync(context, request);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
