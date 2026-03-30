// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    /// <summary>
    /// Http request consisting of just the url without any payload.
    /// </summary>
    public abstract class HttpRequest
    {
        public abstract string Verb { get; }

        public abstract string Url { get; }

        public Dictionary<string, string> Headers { get; set; }
    }

    /// <summary>
    /// Http request with a url and payload.
    /// </summary>
    public abstract class HttpRequest<TPayload> : HttpRequest
    {
        public TPayload Payload { get; set; }
    }
}
