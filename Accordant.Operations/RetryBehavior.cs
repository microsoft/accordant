// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// This lambda indicates whether to retry an operation call, given its response
/// and the number of time this operation call has already been retried.
/// </summary>
public delegate RetryBehavior ShouldRetryOperationLambda(
    IOperation operation,
    object request,
    object response,
    int numExecutions);

/// <summary>
/// This class indicates whether an operation should be retried.
/// </summary>
public class RetryBehavior
{
    /// <summary>
    /// Indicates whether the operation should be retried.
    /// </summary>
    public bool ShouldRetry { get; set; } = false;

    /// <summary>
    /// The amount of time to wait before retrying the operation.
    /// </summary>
    public int WaitTimeInMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Indicates whether retries have been exhausted and the test should now be marked
    /// as failed.
    /// </summary>
    public bool ShouldFailTest { get; set; } = false;
}
