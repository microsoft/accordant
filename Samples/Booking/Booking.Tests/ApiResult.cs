// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Tests;

/// <summary>
/// Generic API result wrapper that captures success data or error information.
/// This is a client-side concern for wrapping HTTP responses.
/// </summary>
public class ApiResult<T>
{
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public string? Error { get; set; }

    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsNotFound => StatusCode == 404;
    public bool IsConflict => StatusCode == 409;
    public bool IsBadRequest => StatusCode == 400;
}
