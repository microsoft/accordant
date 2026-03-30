// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;

    /// <summary>
    /// Selects which request derivations should be generated during test case generation.
    /// Use <see cref="DerivationSelector.For(string)"/> to create instances with a fluent API.
    /// </summary>
    /// <remarks>
    /// When generating test cases, derivations can produce many combinations. Use this selector
    /// to filter which derivations are actually generated:
    /// <list type="bullet">
    /// <item>Filter by target operation (which operation is being derived)</item>
    /// <item>Filter by source operation (what it derives from)</item>
    /// <item>Filter by variant (e.g., "IfMatch" vs "IfNoneMatch")</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// options.DerivationSelectors = new[]
    /// {
    ///     DerivationSelector.For("Delete"),                                  // All Delete derivations
    ///     DerivationSelector.For("Delete").From("Create"),                   // Delete from Create only
    ///     DerivationSelector.For("Delete").From("Create").Variant("IfMatch") // Specific variant
    /// };
    /// </code>
    /// </example>
    public class DerivationSelector
    {
        /// <summary>
        /// The name of the operation whose requests are being derived.
        /// This is the "target" of the derivation.
        /// </summary>
        public string TargetOperation { get; set; }

        /// <summary>
        /// The operations that the derivation sources from.
        /// If null, matches derivations from any source operation.
        /// </summary>
        /// <remarks>
        /// Currently, only single-source derivations are supported at runtime.
        /// This is a list to support future multi-source derivations.
        /// </remarks>
        public IList<string> FromOperations { get; set; }

        /// <summary>
        /// The specific variant label to match.
        /// If null, matches all variants.
        /// </summary>
        public string Variant { get; set; }

        /// <summary>
        /// Creates a new empty DerivationSelector.
        /// Prefer using <see cref="For(string)"/> for better readability.
        /// </summary>
        public DerivationSelector()
        {
        }

        /// <summary>
        /// Start building a selector for derivations targeting the specified operation.
        /// </summary>
        /// <param name="targetOperation">The operation whose requests are being derived.</param>
        /// <returns>A builder for further configuration.</returns>
        public static DerivationSelectorBuilder For(string targetOperation) =>
            new DerivationSelectorBuilder(targetOperation);
    }

    /// <summary>
    /// Fluent builder for <see cref="DerivationSelector"/>.
    /// </summary>
    public class DerivationSelectorBuilder
    {
        private readonly string _targetOperation;
        private IList<string> _fromOperations;
        private string _variant;

        internal DerivationSelectorBuilder(string targetOperation)
        {
            _targetOperation = targetOperation;
        }

        /// <summary>
        /// Filter to derivations from a specific source operation.
        /// </summary>
        /// <param name="operationName">The source operation name.</param>
        public DerivationSelectorBuilder From(string operationName)
        {
            _fromOperations = new[] { operationName };
            return this;
        }

        /// <summary>
        /// Filter to derivations from specific source operations (for future multi-source support).
        /// </summary>
        /// <param name="operationNames">The source operation names.</param>
        public DerivationSelectorBuilder From(params string[] operationNames)
        {
            _fromOperations = operationNames;
            return this;
        }

        /// <summary>
        /// Filter to a specific variant label.
        /// </summary>
        /// <param name="variant">The variant label (e.g., "IfMatch", "IfNoneMatch").</param>
        public DerivationSelectorBuilder Variant(string variant)
        {
            _variant = variant;
            return this;
        }

        /// <summary>
        /// Implicitly convert the builder to a <see cref="DerivationSelector"/>.
        /// </summary>
        public static implicit operator DerivationSelector(DerivationSelectorBuilder builder) =>
            new DerivationSelector
            {
                TargetOperation = builder._targetOperation,
                FromOperations = builder._fromOperations,
                Variant = builder._variant
            };
    }
}
