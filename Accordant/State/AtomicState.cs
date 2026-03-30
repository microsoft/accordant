// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// AtomicState represents values that are 'atoms' and immutable.
    /// It is up to the user to choose what's an atomic value - it could be a
    /// string, a boolean, or even a list of bytes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AtomicState<T> : State
    {
        private Func<T, string> customStringRepresentation;

        /// <summary>
        /// The underlying atomic value.
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// Creates an instance of an AtomicState object.
        /// </summary>
        public AtomicState(T value, Func<T, string> customStringRepresentation = null)
        {
            Value = value;
            Locked = true;
            this.customStringRepresentation = customStringRepresentation;
        }

        /// <summary>
        /// Clones the atomic state.
        /// </summary>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            clonedMap[this] = new AtomicState<T>(Value)
            {
                stateHash = stateHash,
                stringRepresentation = stringRepresentation,
                customStringRepresentation = customStringRepresentation
            };
        }

        /// <summary>
        /// AtomicState does not consist of any sub-components so this
        /// method is a no-op.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
        }

        /// <summary>
        /// Returns a string representation of the AtomicState object.
        /// It is important to ensure the <see cref="object.ToString"/> method of the value
        /// returns a proper representation of the contents of the value as the
        /// string representation is used for calculating the state hash.
        /// </summary>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            if (customStringRepresentation != null)
            {
                return customStringRepresentation(Value);
            }

            if (Value == null)
            {
                return "<null>";
            }

            if (Value is string)
            {
                return "\"" + Value + "\"";
            }

            return Value.ToString();
        }
    }
}
