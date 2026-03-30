// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Labels for derived request variants.
    /// </summary>
    public static class DerivationLabels
    {
        /// <summary>
        /// The default label used when a derivation produces a single request.
        /// </summary>
        public const string Default = "Default";
    }

    /// <summary>
    /// Provides typed access to source operation requests and responses
    /// for multi-source derivations.
    /// </summary>
    public class DerivationSources
    {
        private readonly Dictionary<string, (object Request, object Response)> _sources;

        internal DerivationSources(Dictionary<string, (object Request, object Response)> sources)
        {
            _sources = sources;
        }

        /// <summary>
        /// Gets the response from a source operation, cast to the specified type.
        /// </summary>
        public TResponse ResponseOf<TResponse>(string operationName) =>
            (TResponse)_sources[operationName].Response;

        /// <summary>
        /// Gets the request from a source operation, cast to the specified type.
        /// </summary>
        public TRequest RequestOf<TRequest>(string operationName) =>
            (TRequest)_sources[operationName].Request;

        /// <summary>
        /// Gets both the request and response from a source operation.
        /// </summary>
        public (TRequest Request, TResponse Response) Get<TRequest, TResponse>(string operationName) =>
            ((TRequest)_sources[operationName].Request, (TResponse)_sources[operationName].Response);

        /// <summary>
        /// Gets the raw request/response pair for a source operation.
        /// </summary>
        internal (object Request, object Response) GetRaw(string operationName) =>
            _sources[operationName];
    }

    /// <summary>
    /// Defines how a request can be derived from another operation's request/response.
    /// Use the <see cref="Derive"/> class to create instances.
    /// </summary>
    public class RequestDerivation
    {
        /// <summary>
        /// The operation names this derivation depends on.
        /// </summary>
        public IList<string> Sources => FromOperations;

        /// <summary>
        /// Internal list of operation names this derivation depends on.
        /// </summary>
        internal IList<string> FromOperations { get; set; }

        /// <summary>
        /// Internal function that generates derived request(s).
        /// Parameters: (sources, template) => Dictionary of label -> request
        /// </summary>
        internal Func<DerivationSources, object, Dictionary<string, object>> DerivationFunc { get; set; }

        private RequestDerivation() { }

        internal static RequestDerivation Create(
            IList<string> fromOperations,
            Func<DerivationSources, object, Dictionary<string, object>> derivationFunc)
        {
            return new RequestDerivation
            {
                FromOperations = fromOperations,
                DerivationFunc = derivationFunc
            };
        }

        /// <summary>
        /// Derives request(s) from the given source operation results.
        /// </summary>
        /// <param name="sources">Dictionary mapping operation name to (Request, Response) tuple.</param>
        /// <param name="template">Optional template value for derivations that use templates.</param>
        /// <returns>Dictionary mapping variant label to derived request.</returns>
        public Dictionary<string, object> Derive(
            Dictionary<string, (object Request, object Response)> sources,
            object template = null)
        {
            return DerivationFunc(new DerivationSources(sources), template);
        }

        /// <summary>
        /// Invokes the derivation function with the given sources and template.
        /// </summary>
        internal Dictionary<string, object> Invoke(
            Dictionary<string, (object Request, object Response)> sources,
            object template)
        {
            return DerivationFunc(new DerivationSources(sources), template);
        }
    }

    /// <summary>
    /// Fluent API for creating request derivations with types on the left side.
    /// </summary>
    /// <typeparam name="TReq">The source operation's request type.</typeparam>
    /// <typeparam name="TResp">The source operation's response type.</typeparam>
    /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
    /// <example>
    /// <code>
    /// // Types on the left (like a type declaration)
    /// Derivation&lt;Todo, ApiResult&lt;Todo&gt;, (string, string)&gt;.From("CreateTodo")
    ///     .When((req, resp) => resp.IsSuccess)
    ///     .As((req, resp) => (resp.Data.UserId, resp.Data.TodoId))
    /// </code>
    /// </example>
    public static class Derivation<TReq, TResp, TResult>
    {
        /// <summary>
        /// Start defining a derivation from a single source operation.
        /// </summary>
        /// <param name="operationName">The name of the operation to derive from.</param>
        public static TypedSingleSourceDerivationBuilder<TReq, TResp, TResult> From(string operationName) =>
            new TypedSingleSourceDerivationBuilder<TReq, TResp, TResult>(operationName);
    }

    /// <summary>
    /// Fluent API for creating request derivations.
    /// </summary>
    /// <example>
    /// <code>
    /// // Types on the right (method style)
    /// Derive.From&lt;CreateRequest, CreateResponse, DeleteRequest&gt;("CreateBlog")
    ///       .When((req, resp) => resp.Success)
    ///       .As((req, resp) => new DeleteRequest { Id = resp.Id })
    /// 
    /// // Legacy: Types on As()
    /// Derive.From("CreateBlog")
    ///       .As&lt;CreateRequest, CreateResponse, DeleteRequest&gt;((req, resp) => new DeleteRequest { Id = resp.Id })
    /// </code>
    /// </example>
    public static class Derive
    {
        /// <summary>
        /// Start defining a typed derivation from a single source operation.
        /// This is the preferred API as it enables When() filtering and cleaner As() calls.
        /// </summary>
        /// <typeparam name="TReq">The source operation's request type.</typeparam>
        /// <typeparam name="TResp">The source operation's response type.</typeparam>
        /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
        /// <param name="operationName">The name of the operation to derive from.</param>
        public static TypedSingleSourceDerivationBuilder<TReq, TResp, TResult> From<TReq, TResp, TResult>(
            string operationName) =>
            new TypedSingleSourceDerivationBuilder<TReq, TResp, TResult>(operationName);

        /// <summary>
        /// Start defining a derivation from a single source operation (legacy API).
        /// Types are specified on As() method instead.
        /// </summary>
        /// <param name="operationName">The name of the operation to derive from.</param>
        public static SingleSourceDerivationBuilder From(string operationName) =>
            new SingleSourceDerivationBuilder(operationName);

        /// <summary>
        /// Start defining a derivation from multiple source operations.
        /// Note: Multi-source derivations are not yet implemented at runtime.
        /// </summary>
        /// <param name="operationNames">The names of the operations to derive from.</param>
        public static MultiSourceDerivationBuilder From(params string[] operationNames)
        {
            if (operationNames.Length == 1)
            {
                throw new ArgumentException(
                    "Use From(string) for single-source derivations. " +
                    "Multi-source derivations require 2+ operations.");
            }
            return new MultiSourceDerivationBuilder(operationNames);
        }
    }

    /// <summary>
    /// Builder for typed single-source derivations with all type parameters specified upfront.
    /// This enables cleaner When() and As() calls without type parameters.
    /// </summary>
    /// <typeparam name="TReq">The source operation's request type.</typeparam>
    /// <typeparam name="TResp">The source operation's response type.</typeparam>
    /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
    public class TypedSingleSourceDerivationBuilder<TReq, TResp, TResult>
    {
        private readonly string _operationName;

        internal TypedSingleSourceDerivationBuilder(string operationName)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        }

        /// <summary>
        /// Add a filter condition. Derivation is skipped when predicate returns false.
        /// </summary>
        /// <param name="predicate">Condition that must be true for derivation to proceed.</param>
        public TypedWhenBuilder<TReq, TResp, TResult> When(Func<TReq, TResp, bool> predicate) =>
            new TypedWhenBuilder<TReq, TResp, TResult>(_operationName, predicate);

        /// <summary>
        /// Define the derivation that creates a single request (no template, no filter).
        /// </summary>
        /// <param name="factory">Function that creates the derived request from source request/response.</param>
        public RequestDerivation As(Func<TReq, TResp, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp)
                    };
                });
        }

        /// <summary>
        /// Define the derivation that creates a single request with template support (no filter).
        /// </summary>
        /// <param name="factory">Function that creates the derived request from source request/response and template.</param>
        public RequestDerivation As(Func<TReq, TResp, TResult, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp, (TResult)template)
                    };
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants (no template, no filter).
        /// Use this for cases like generating both IfMatch and IfNoneMatch requests.
        /// </summary>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants(Func<TReq, TResp, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    var variants = factory(req, resp);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants with template support (no filter).
        /// </summary>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants(Func<TReq, TResp, TResult, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    var variants = factory(req, resp, (TResult)template);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }
    }

    /// <summary>
    /// Builder for typed derivations with a When() filter applied.
    /// </summary>
    /// <typeparam name="TReq">The source operation's request type.</typeparam>
    /// <typeparam name="TResp">The source operation's response type.</typeparam>
    /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
    public class TypedWhenBuilder<TReq, TResp, TResult>
    {
        private readonly string _operationName;
        private readonly Func<TReq, TResp, bool> _predicate;

        internal TypedWhenBuilder(string operationName, Func<TReq, TResp, bool> predicate)
        {
            _operationName = operationName;
            _predicate = predicate;
        }

        /// <summary>
        /// Define the derivation that creates a single request when the filter passes.
        /// Returns empty (skips derivation) when the filter returns false.
        /// </summary>
        /// <param name="factory">Function that creates the derived request from source request/response.</param>
        public RequestDerivation As(Func<TReq, TResp, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);

                    if (!_predicate(req, resp))
                        return new Dictionary<string, object>();

                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp)
                    };
                });
        }

        /// <summary>
        /// Define the derivation that creates a single request with template support when the filter passes.
        /// Returns empty (skips derivation) when the filter returns false.
        /// </summary>
        /// <param name="factory">Function that creates the derived request from source request/response and template.</param>
        public RequestDerivation As(Func<TReq, TResp, TResult, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);

                    if (!_predicate(req, resp))
                        return new Dictionary<string, object>();

                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp, (TResult)template)
                    };
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants when the filter passes.
        /// Returns empty (skips derivation) when the filter returns false.
        /// </summary>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants(Func<TReq, TResp, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);

                    if (!_predicate(req, resp))
                        return new Dictionary<string, object>();

                    var variants = factory(req, resp);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants with template support when the filter passes.
        /// Returns empty (skips derivation) when the filter returns false.
        /// </summary>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants(Func<TReq, TResp, TResult, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);

                    if (!_predicate(req, resp))
                        return new Dictionary<string, object>();

                    var variants = factory(req, resp, (TResult)template);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }
    }

    /// <summary>
    /// Builder for single-source derivations (legacy API - types specified on As()).
    /// </summary>
    public class SingleSourceDerivationBuilder
    {
        private readonly string _operationName;

        internal SingleSourceDerivationBuilder(string operationName)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        }

        /// <summary>
        /// Define the derivation that creates a single request (no template).
        /// </summary>
        /// <typeparam name="TReq">The source operation's request type.</typeparam>
        /// <typeparam name="TResp">The source operation's response type.</typeparam>
        /// <typeparam name="TResult">The derived request type.</typeparam>
        /// <param name="factory">Function that creates the derived request from source request/response.</param>
        public RequestDerivation As<TReq, TResp, TResult>(
            Func<TReq, TResp, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp)
                    };
                });
        }

        /// <summary>
        /// Define the derivation that creates a single request with template support.
        /// The template is provided externally via <see cref="TestGenerationOptions.RequestTemplates"/>.
        /// </summary>
        /// <typeparam name="TReq">The source operation's request type.</typeparam>
        /// <typeparam name="TResp">The source operation's response type.</typeparam>
        /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
        /// <param name="factory">Function that creates the derived request from source request/response and template.</param>
        public RequestDerivation As<TReq, TResp, TResult>(
            Func<TReq, TResp, TResult, TResult> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    return new Dictionary<string, object>
                    {
                        [DerivationLabels.Default] = factory(req, resp, (TResult)template)
                    };
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants (no template).
        /// Use this for cases like generating both IfMatch and IfNoneMatch requests,
        /// or return an empty dictionary to skip derivation.
        /// </summary>
        /// <typeparam name="TReq">The source operation's request type.</typeparam>
        /// <typeparam name="TResp">The source operation's response type.</typeparam>
        /// <typeparam name="TResult">The derived request type.</typeparam>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants<TReq, TResp, TResult>(
            Func<TReq, TResp, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    var variants = factory(req, resp);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants with template support.
        /// </summary>
        /// <typeparam name="TReq">The source operation's request type.</typeparam>
        /// <typeparam name="TResp">The source operation's response type.</typeparam>
        /// <typeparam name="TResult">The derived request type (also used as template type).</typeparam>
        /// <param name="factory">Function that creates a dictionary of variant label to derived request.</param>
        public RequestDerivation AsVariants<TReq, TResp, TResult>(
            Func<TReq, TResp, TResult, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                new List<string> { _operationName },
                (sources, template) =>
                {
                    var (req, resp) = sources.Get<TReq, TResp>(_operationName);
                    var variants = factory(req, resp, (TResult)template);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }
    }

    /// <summary>
    /// Builder for multi-source derivations.
    /// Note: Multi-source derivations are not yet implemented at runtime.
    /// </summary>
    public class MultiSourceDerivationBuilder
    {
        private readonly IList<string> _operationNames;

        internal MultiSourceDerivationBuilder(IList<string> operationNames)
        {
            _operationNames = operationNames;
        }

        /// <summary>
        /// Define the derivation that creates a single request from multiple sources (no template).
        /// </summary>
        public RequestDerivation As<TResult>(Func<DerivationSources, TResult> factory)
        {
            return RequestDerivation.Create(
                _operationNames,
                (sources, template) => new Dictionary<string, object>
                {
                    [DerivationLabels.Default] = factory(sources)
                });
        }

        /// <summary>
        /// Define the derivation that creates a single request from multiple sources with template support.
        /// </summary>
        public RequestDerivation As<TResult>(
            Func<DerivationSources, TResult, TResult> factory)
        {
            return RequestDerivation.Create(
                _operationNames,
                (sources, template) => new Dictionary<string, object>
                {
                    [DerivationLabels.Default] = factory(sources, (TResult)template)
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants from multiple sources (no template).
        /// </summary>
        public RequestDerivation AsVariants<TResult>(
            Func<DerivationSources, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                _operationNames,
                (sources, template) =>
                {
                    var variants = factory(sources);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }

        /// <summary>
        /// Define a derivation that produces multiple request variants from multiple sources with template support.
        /// </summary>
        public RequestDerivation AsVariants<TResult>(
            Func<DerivationSources, TResult, Dictionary<string, TResult>> factory)
        {
            return RequestDerivation.Create(
                _operationNames,
                (sources, template) =>
                {
                    var variants = factory(sources, (TResult)template);
                    var result = new Dictionary<string, object>();
                    foreach (var kv in variants)
                        result[kv.Key] = kv.Value;
                    return result;
                });
        }
    }
}
