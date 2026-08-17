// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Adapters.OpenApi;

/// <summary>
/// Thrown when <see cref="OpenApiTargetAdapter"/>'s <c>{ "document": ..., "baseUrl": ... }</c>
/// settings are missing, malformed, or otherwise invalid - for example a non-object
/// settings value, a blank or missing <c>document</c>/<c>baseUrl</c>, a <c>document</c>
/// that does not resolve to an existing file, or a <c>baseUrl</c> that is not an absolute
/// <c>http</c>/<c>https</c> URL.
/// </summary>
public sealed class OpenApiSettingsException : ArgumentException
{
    /// <summary>
    /// Creates an exception describing why the adapter's settings are invalid.
    /// </summary>
    /// <param name="message">A message describing the specific validation failure.</param>
    public OpenApiSettingsException(string message)
        : base(message, "settings")
    {
    }
}

/// <summary>
/// Thrown when an OpenAPI document cannot be interpreted by this adapter: it is not a
/// supported OpenAPI 3.0.x document, an operation is missing a nonblank
/// <c>operationId</c> or declares one that duplicates another operation's, a schema
/// reference is unsupported (non-local, undefined, or cyclic), or an operation uses a
/// parameter location or request body content type this adapter does not support.
///
/// This adapter deliberately fails clearly on any of these rather than inventing an
/// unstable operation name or silently ignoring a construct it cannot represent.
/// </summary>
public sealed class OpenApiDocumentException : FormatException
{
    /// <summary>
    /// Creates an exception describing why the OpenAPI document could not be interpreted.
    /// </summary>
    /// <param name="message">A message describing the specific parsing failure.</param>
    public OpenApiDocumentException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when executing an operation fails for a reason specific to this adapter's own
/// HTTP execution - most notably, a response that has a nonempty body which is not valid
/// JSON. This adapter never silently reinterprets such a response as an empty or
/// otherwise-shaped body: a malformed response body is a genuine execution failure, and,
/// like a network or client-level failure, is left as an exception that interrupts the
/// current trace rather than being turned into a successful (if fabricated) response.
/// </summary>
public sealed class OpenApiExecutionException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing why executing an operation failed.
    /// </summary>
    /// <param name="message">A message describing the specific execution failure.</param>
    /// <param name="innerException">The underlying exception, if any (e.g. a JSON parsing error).</param>
    public OpenApiExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
