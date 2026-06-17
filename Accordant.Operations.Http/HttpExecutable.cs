// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// This class sends requests encoded through <see cref="HttpRequest"/> objects
/// using an <see cref="HttpClient"/> retrieved via <see cref="TestingContext.Target{T}"/>.
/// The results are wrapped in <see cref="HttpResponse"/> objects and returned to the
/// caller.
/// </summary>
public class HttpExecutable
{
    /// <summary>
    /// The payload serializer takes in an object and returns a <see cref="StringContent"/>
    /// object which is used to pass the payload as the body in an <see cref="HttpRequestMessage"/>
    /// object.
    /// </summary>
    public Func<object, HttpContent> PayloadSerializer { get; set; }

    /// <summary>
    /// The payload deserializer takes in a string read from the <see cref="HttpResponseMessage"/>
    /// object along with the expected type of the response and returns a deserialized object.
    /// </summary>
    public Func<HttpContent, Type, object> PayloadDeserializer { get; set; }

    /// <summary>
    /// This flag controls whether this class emits diagnostic logs when executing
    /// HTTP requests.
    /// </summary>
    public bool EmitDiagnosticLogs { get; set; } = false;

    /// <summary>
    /// This classes uses this lambda to emit log lines.
    /// </summary>
    public Action<string> LogLineEmitter { get; set; } = (logLine) => Logger.Log(logLine);

    /// <summary>
    /// Returns the default instance of this class.
    ///
    /// The default instance serializes payloads using <see cref="System.Text.Json.JsonSerializer"/>
    /// class, encoded as a UTF8 string with ContentType set to 'application/json'.
    ///
    /// The default instance deserializes the payload assuming the string is encoded as a JSON object.
    /// It uses <see cref="System.Text.Json.JsonSerializer"/> to deserialize the payload, treating
    /// property names as case insensitive.
    /// </summary>
    public static HttpExecutable Default { get; set; } = new HttpExecutable();

    /// <summary>
    /// The maximum length of a log line after which it is trimmed. Defaults to 2KB.
    /// </summary>
    public int MaxLogLineLength { get; set; } = 2 * 1024;

    /// <summary>
    /// Whether to trim log lines if there length exceeds <see cref="MaxLogLineLength"/>
    /// </summary>
    public bool TrimLogLinesIfTooLong { get; set; } = true;

    /// <summary>
    /// Indicates whether correlation id should be emitted in HTTP diagnostic logs.
    /// Correlation Ids can be particularly helpful when debugging logs of requests
    /// made concurrently with each other.
    /// </summary>
    public bool EmitCorrelationId { get; set; } = false;

    /// <summary>
    /// Constructs an instance of this class, taking in an optional
    /// pair of payload serializer and deserializer lambdas.
    /// </summary>
    public HttpExecutable()
    {
        PayloadSerializer = (payload) =>
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

        PayloadDeserializer = (responseContent, payloadType) =>
            JsonSerializer.Deserialize(
                responseContent.ReadAsStringAsync().Result,
                payloadType,
                new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// This method takes an http request with a payload of type TRequest and returns
    /// an http response containing a payload of type TResponse.
    /// </summary>
    public async Task<HttpResponse<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TestingContext context,
        HttpRequest<TRequest> httpRequest,
        Func<int, bool> shouldParsePayload = null,
        Dictionary<string, string> additionalRequestHeaders = null)
    {
        var httpResponse = new HttpResponse<TResponse>();

        var (retrievedPayload, responsePayload) = await ExecuteAsyncInternal(
            context,
            httpRequest,
            httpResponse,
            sendPayload: typeof(TRequest) != typeof(Unit),
            requestPayload: httpRequest.Payload,
            retrievePayload: true,
            typeof(TResponse),
            shouldParsePayload,
            additionalRequestHeaders: additionalRequestHeaders);

        if (retrievedPayload)
        {
            httpResponse.Payload = (TResponse)responsePayload;
        }

        return httpResponse;
    }

    /// <summary>
    /// This method takes an http request with a payload of type TRequest and returns
    /// an http response without any payload.
    /// </summary>
    public async Task<HttpResponse> ExecuteAsync<TRequest, TEmpty>(
        TestingContext context,
        HttpRequest<TRequest> httpRequest,
        Dictionary<string, string> additionalRequestHeaders = null)
        where TEmpty : Unit
    {
        var httpResponse = new HttpResponse();

        _ = await ExecuteAsyncInternal(
            context,
            httpRequest,
            httpResponse,
            sendPayload: true,
            requestPayload: httpRequest.Payload,
            retrievePayload: false,
            responsePayloadType: null,
            additionalRequestHeaders: additionalRequestHeaders);

        return httpResponse;
    }

    /// <summary>
    /// This method takes an http request with no payload and returns
    /// an http response containing a payload of type TResponse.
    /// </summary>
    public async Task<HttpResponse<TResponse>> ExecuteAsync<TEmpty, TResponse>(
        TestingContext context,
        HttpRequest httpRequest,
        Func<int, bool> shouldParsePayload = null,
        Dictionary<string, string> additionalRequestHeaders = null)
        where TEmpty : Unit
    {
        var httpResponse = new HttpResponse<TResponse>();

        var (retrievedPayload, responsePayload) = await ExecuteAsyncInternal(
            context,
            httpRequest,
            httpResponse,
            sendPayload: false,
            requestPayload: null,
            retrievePayload: true,
            typeof(TResponse),
            shouldParsePayload,
            additionalRequestHeaders: additionalRequestHeaders);

        if (retrievedPayload)
        {
            httpResponse.Payload = (TResponse)responsePayload;
        }

        return httpResponse;
    }

    /// <summary>
    /// This method takes an http request with no payload and returns an
    /// http response with no payload.
    /// </summary>
    public async Task<HttpResponse> ExecuteAsync(
        TestingContext context,
        HttpRequest httpRequest,
        Dictionary<string, string> additionalRequestHeaders = null)
    {
        var httpResponse = new HttpResponse();

        _ = await ExecuteAsyncInternal(
            context,
            httpRequest,
            httpResponse,
            sendPayload: false,
            requestPayload: null,
            retrievePayload: false,
            responsePayloadType: null,
            additionalRequestHeaders: additionalRequestHeaders);

        return httpResponse;
    }

    private async Task<(bool, object)> ExecuteAsyncInternal(
        TestingContext context,
        HttpRequest httpRequest,
        HttpResponse httpResponse,
        bool sendPayload,
        object requestPayload,
        bool retrievePayload,
        Type responsePayloadType,
        Func<int, bool> shouldParsePayload = null,
        Dictionary<string, string> additionalRequestHeaders = null)
    {
        bool retrievedPayload = false;
        object responsePayload = null;

        // Get HttpClient from context using Get<T>()
        var httpClient = context.Get<HttpClient>();

        var httpRequestMessage = new HttpRequestMessage(
            new HttpMethod(httpRequest.Verb),
            new Uri(httpRequest.Url, UriKind.RelativeOrAbsolute));

        static void SetHeaders(
            HttpRequestMessage requestMessage,
            Dictionary<string, string> headers)
        {
            if (headers == null)
            {
                return;
            }

            foreach (var kvp in headers)
            {
                if (!kvp.Key.StartsWith("Content-") && kvp.Key != "Trailer" && kvp.Key != "Transfer-Encoding")
                {
                    bool result = requestMessage.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    if (!result)
                    {
                        throw new HttpExecutableException($"Could not add the following key value pair to http request headers. Key: {kvp.Key} and Value: {kvp.Value}");
                    }
                }
            }
        }

        static void SetContentHeaders(
            HttpRequestMessage requestMessage,
            Dictionary<string, string> headers)
        {
            if (headers == null)
            {
                return;
            }

            foreach (var kvp in headers)
            {
                if (kvp.Key.StartsWith("Content-") || kvp.Key == "Trailer" || kvp.Key == "Transfer-Encoding")
                {
                    requestMessage.Content.Headers.Add(kvp.Key, kvp.Value);
                }
            }
        }

        SetHeaders(httpRequestMessage, httpRequest.Headers);
        SetHeaders(httpRequestMessage, additionalRequestHeaders);

        string serializedRequestBody = null;
        if (sendPayload)
        {
            httpRequestMessage.Content = PayloadSerializer(requestPayload);
            SetContentHeaders(httpRequestMessage, httpRequest.Headers);
            SetContentHeaders(httpRequestMessage, additionalRequestHeaders);

            serializedRequestBody = await httpRequestMessage.Content.ReadAsStringAsync();
        }

        var correlationId = Guid.NewGuid().ToString();

        if (EmitDiagnosticLogs)
        {
            EmitLog(correlationId, $"Executing {httpRequest.Verb} {httpRequest.Url}");
            if (httpRequestMessage.Headers.Count() > 0 ||
                httpRequestMessage.Content?.Headers.Count() > 0)
            {
                var headersString = HeadersToString(httpRequestMessage.Headers);
                var contentHeadersString = httpRequestMessage.Content != null ?
                    HeadersToString(httpRequestMessage.Content.Headers) :
                    string.Empty;

                EmitLog(correlationId, $"Headers: {string.Join(", ", headersString, contentHeadersString)}");
            }
            EmitLog(correlationId, sendPayload ?
                $"Request Payload: {serializedRequestBody}" :
                "Request Payload: <empty>");
        }

        var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);
        httpResponse.StatusCode = (int)httpResponseMessage.StatusCode;
        if (httpResponse.Headers == null)
        {
            httpResponse.Headers = new Dictionary<string, string>();
        }

        foreach (var kvp in httpResponseMessage.Headers)
        {
            httpResponse.Headers.Add(kvp.Key, string.Join(",", kvp.Value));
        }

        foreach (var kvp in httpResponseMessage.Content.Headers)
        {
            httpResponse.Headers.Add(kvp.Key, string.Join(",", kvp.Value));
        }

        var serializedResponseBody = await httpResponseMessage.Content.ReadAsStringAsync();

        if (retrievePayload)
        {
            if (shouldParsePayload == null ||
                shouldParsePayload((int)httpResponseMessage.StatusCode))
            {
                responsePayload = PayloadDeserializer(
                    httpResponseMessage.Content,
                    responsePayloadType);

                retrievedPayload = true;
            }
        }

        if (EmitDiagnosticLogs)
        {
            EmitLog(correlationId, $"Received response with HTTP status code {httpResponseMessage.StatusCode}");

            if (httpResponseMessage.Headers.Count() > 0 ||
                httpResponseMessage.Content?.Headers.Count() > 0)
            {
                var headersString = HeadersToString(httpResponseMessage.Headers);
                var contentHeadersString = httpResponseMessage.Content != null ?
                    HeadersToString(httpResponseMessage.Content.Headers) :
                    string.Empty;

                EmitLog(correlationId, $"Headers: {string.Join(", ", headersString, contentHeadersString)}");
            }

            EmitLog(correlationId, $"Serialized response body: {serializedResponseBody}");
            EmitLog(correlationId, retrievedPayload ?
                $"Successfully parsed the response body into type {responsePayloadType.Name}" :
                "Response body not parsed into any object.");
        }

        return (retrievedPayload, responsePayload);
    }

    private void EmitLog(string correlationId, string logLine)
    {
        var prefix = "[HTTP Diagnostics] ";
        if (EmitCorrelationId)
        {
            prefix += $"[Correlation Id: {correlationId}] ";
        }

        LogLineEmitter(prefix + TrimLogLineIfRequired(logLine));
    }

    private string TrimLogLineIfRequired(string logLine)
    {
        if (logLine == null)
        {
            return logLine;
        }

        if (logLine.Length <= MaxLogLineLength ||
            !TrimLogLinesIfTooLong)
        {
            return logLine;
        }

        int trimmedChars = logLine.Length - MaxLogLineLength;

        return $"[Trimmed {BytesToHigherUnits(trimmedChars)} of {BytesToHigherUnits(logLine.Length)} log line] {logLine.Substring(0, MaxLogLineLength)}...";
    }

    public static string BytesToHigherUnits(long numBytes)
    {
        if (numBytes < 1024)
        {
            return numBytes.ToString();
        }
        else if (numBytes < 1024 * 1024)
        {
            return Math.Round((double)numBytes / 1024, 2) + " KB";
        }
        else if (numBytes < 1024 * 1024 * 1024)
        {
            return Math.Round((double)numBytes / (1024 * 1024), 2) + " MB";
        }
        else
        {
            return Math.Round((double)numBytes / (1024 * 1024 * 1024), 2) + " GB";
        }
    }

    private static string HeadersToString(HttpHeaders headers)
    {
        return string.Join("; ", headers.Select(
            kv => kv.Key == "Authorization" ?
            $"{kv.Key}: <hidden>" :
            $"{kv.Key}: {string.Join(", ", kv.Value)}"));
    }
}
