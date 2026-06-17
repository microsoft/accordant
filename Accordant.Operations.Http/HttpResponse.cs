// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;
using System.Net;

/// <summary>
/// Http response consisting of just the status code.
/// </summary>
public class HttpResponse
{
    public int StatusCode { get; set; }

    public Dictionary<string, string> Headers { get; set; }

    public HttpResponse()
    {
    }

    public HttpResponse(int statusCode, Dictionary<string, string> headers = null)
    {
        StatusCode = statusCode;
        Headers = headers;
    }

    public HttpResponse(HttpStatusCode statusCode, Dictionary<string, string> headers = null)
        : this((int)statusCode, headers)
    {
    }
}

/// <summary>
/// Http response consisting of the status code and a payload.
/// </summary>
public class HttpResponse<TPayload> : HttpResponse
{
    public TPayload Payload { get; set; }

    public HttpResponse()
    {
    }

    public HttpResponse(int statusCode, Dictionary<string, string> headers = null)
        : base(statusCode, headers)
    {
    }

    public HttpResponse(HttpStatusCode statusCode, Dictionary<string, string> headers = null)
        : this((int)statusCode, headers)
    {
    }
}
