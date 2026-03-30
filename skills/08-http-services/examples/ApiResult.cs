// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples.Http;

/// <summary>
/// Generic API result wrapper that captures success data or error information.
/// Use this pattern for simple, clear HTTP response handling.
/// </summary>
public class ApiResult<T>
{
    /// <summary>
    /// The response data (null on error).
    /// </summary>
    public T? Data { get; set; }
    
    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int StatusCode { get; set; }
    
    /// <summary>
    /// Error message (null on success).
    /// </summary>
    public string? Error { get; set; }

    // Convenience properties for common status checks
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
    public bool IsUnauthorized => StatusCode == 401;
    public bool IsForbidden => StatusCode == 403;
}
