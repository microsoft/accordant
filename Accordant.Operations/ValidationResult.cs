// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    /// <summary>
    /// Represents the result of validating a response against expected behavior.
    /// Can be implicitly converted from a bool for simple cases.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Indicates whether the validation passed.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// An optional message explaining why validation failed.
        /// Only meaningful when IsValid is false.
        /// </summary>
        public string FailureMessage { get; }

        private ValidationResult(bool isValid, string failureMessage = null)
        {
            IsValid = isValid;
            FailureMessage = failureMessage;
        }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static ValidationResult Valid() => new ValidationResult(true);

        /// <summary>
        /// Creates a failed validation result with an optional explanation.
        /// </summary>
        public static ValidationResult Invalid(string message = null) => new ValidationResult(false, message);

        /// <summary>
        /// Implicit conversion from bool for simple validation cases.
        /// </summary>
        /// <example>
        /// return response.StatusCode == 200; // implicitly converts to ValidationResult
        /// </example>
        public static implicit operator ValidationResult(bool isValid)
        {
            return new ValidationResult(isValid);
        }
    }
}
