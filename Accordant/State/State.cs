// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.IO.Hashing;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State class represents the state maintained by the user of a system to make sense
    /// of the behavior of the system. It is not the internal state of the system though
    /// it is probably similar to the internal state of the system.
    /// 
    /// This is an abstract class and defines properties and methods that should be implemented
    /// by all state objects.
    /// </summary>
    public abstract class State : IState
    {
        public static Random Random { get; } = new Random();

        /// <summary>
        /// Controls whether <see cref="ValidateNotMutated"/> performs validation.
        /// Set to false to disable validation for performance in production scenarios.
        /// Default is true.
        /// </summary>
        public static bool EnableFreezeValidation { get; set; } = true;

        protected string stringRepresentation = null;
        protected ulong? stateHash = null;
        private string _frozenFingerprint = null;

        /// <summary>
        /// Indicates whether a state object is frozen (immutable).
        /// State objects are not frozen when initially created so the user
        /// can modify the state in an 'imperative' fashion. They are then frozen
        /// by the framework once returned from the method that created them so they
        /// are effectively immutable. Once frozen, the only way to modify a state object
        /// is to clone it to create a new state object that can be modified till it's also
        /// eventually frozen.
        /// </summary>
        [JsonIgnore]
        public bool IsFrozen { get; protected set; } = false;

        /// <summary>
        /// Clones the state object. The cloned state object is initially not frozen.
        /// </summary>
        /// <returns></returns>
        public virtual State Clone()
        {
            var (clone, _) = CloneWithMap();
            return clone;
        }

        /// <summary>
        /// Explicit interface implementation for IState.Clone().
        /// </summary>
        IState IState.Clone() => Clone();

        /// <summary>
        /// Clones the state object. The cloned state object is initially not frozen.
        /// This also returns a map that maps objects in the original state to corresponding
        /// objects in the cloned state.
        /// </summary>
        public (State, Dictionary<object, object>) CloneWithMap()
        {
            var clonedMap = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            var clone = Clone(clonedMap);
            return (clone, clonedMap);
        }

        public (TState, Dictionary<object, object>) CloneWithMap<TState>()
            where TState : State
        {
            var (state, map) = CloneWithMap();
            return ((TState)state, map);
        }

        /// <summary>
        /// Clones the state object. This method is supposed to be called by
        /// other State objects internally and should not be directly called by
        /// users.
        /// </summary>
        public State Clone(Dictionary<object, object> clonedMap)
        {
            if (!clonedMap.ContainsKey(this))
            {
                CloneInternal(clonedMap);
            }

            if (clonedMap[this] is State state)
            {
                return state;
            }
            else
            {
                throw new InvalidOperationException(
                    $"The cloned map contains an invalid value for key '{GetType().Name}'. " +
                    $"Expected a State instance but found '{clonedMap[this]?.GetType().Name ?? "null"}'.");
            }
        }

        /// <summary>
        /// This method should clone the state set the cloned state in the map.
        /// It does not need to check whether the clone is already present
        /// in clonedMap or not as it is only called if there is not an entry
        /// in clonedMap. It should however set the clone in the map, before calling
        /// the Clone method on its sub-components (as they may recursively point back
        /// to the parent).
        /// </summary>
        /// <param name="clonedMap"></param>
        /// <returns></returns>
        protected abstract void CloneInternal(Dictionary<object, object> clonedMap);

        public override string ToString()
        {
            return StringRepresentation();
        }

        /// <summary>
        /// Returns a string representation of the state. The string representation of the
        /// state is not just a human-friendly text representation but also serves as the value
        /// over which the state hash is computed. Care must be taken to ensure that two different
        /// state objects must have different string representations; they will map to the same state
        /// hash if they are not different.
        /// </summary>
        public string StringRepresentation() => StringRepresentation(forceRecompute: false);

        /// <summary>
        /// Returns the string representation, optionally bypassing the cache.
        /// </summary>
        /// <param name="forceRecompute">If true, recomputes even for frozen objects (propagates to nested states).</param>
        public string StringRepresentation(bool forceRecompute)
        {
            var useCache = IsFrozen && stringRepresentation != null && !forceRecompute;

            if (useCache)
            {
                return stringRepresentation;
            }

            var result = StringRepresentation(
                objectPaths: new Dictionary<object, string>(ReferenceEqualityComparer.Instance),
                path: "$",
                forceRecompute: forceRecompute);
            Invariant.Assert(result != null);

            if (IsFrozen && !forceRecompute)
            {
                stringRepresentation = result;
            }

            return result;
        }

        /// <summary>
        /// See <see cref="StringRepresentation()"/> for details. This method should not be
        /// directly called by users and only by other classes that inherit from the
        /// <see cref="State"/> class.
        /// </summary>
        public string StringRepresentation(
            Dictionary<object, string> objectPaths,
            string path) => StringRepresentation(objectPaths, path, forceRecompute: false);

        /// <summary>
        /// Internal overload with forceRecompute parameter.
        /// </summary>
        public string StringRepresentation(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute)
        {
            if (objectPaths.ContainsKey(this))
            {
                return objectPaths[this];
            }

            objectPaths[this] = path;

            return StringRepresentationInternal(objectPaths, path, forceRecompute);
        }

        /// <summary>
        /// Returns the 64-bit state hash, computed incrementally using XxHash64.
        /// Cached for frozen states.
        /// </summary>
        public ulong GetStateHash()
        {
            if (IsFrozen && stateHash.HasValue)
            {
                return stateHash.Value;
            }

            var hasher = new XxHash64();
            var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            AppendHashCore(hasher, visited);
            var result = hasher.GetCurrentHashAsUInt64();

            if (IsFrozen)
            {
                stateHash = result;
            }

            return result;
        }

        /// <summary>
        /// Appends a length-prefixed string to the hasher.
        /// The length (in bytes) is prepended to prevent boundary collisions
        /// (e.g., "ab" + "c" vs "a" + "bc").
        /// </summary>
        /// <param name="hasher">The XxHash64 hasher to append data to.</param>
        /// <param name="value">The string to append.</param>
        public static void AppendLengthPrefixedString(XxHash64 hasher, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hasher.Append(BitConverter.GetBytes(bytes.Length));
            hasher.Append(bytes);
        }

        /// <summary>
        /// Gets a stable type name that doesn't include assembly-qualified generic type arguments.
        /// For non-generic types, returns the same as Type.FullName.
        /// For closed generics (e.g., Container&lt;ItemState&gt;), Type.FullName includes assembly
        /// version info in the generic arguments, making fingerprints/hashes version-dependent.
        /// This method strips that info, producing a name like:
        ///   "Ns.Container`1[Ns.ItemState]" instead of "Ns.Container`1[[Ns.ItemState, Assembly, Version=...]]"
        /// </summary>
        public static string GetStableTypeName(Type type)
        {
            if (!type.IsGenericType)
                return type.FullName ?? type.Name;

            var genericDef = type.GetGenericTypeDefinition();
            var defName = genericDef.FullName ?? genericDef.Name;
            var args = type.GetGenericArguments();
            var argNames = string.Join(",", args.Select(a => GetStableTypeName(a)));
            return $"{defName}[{argNames}]";
        }

        /// <summary>
        /// Appends this state's hash data to the provided hasher.
        /// This method handles cycle detection and delegates to the generated override.
        /// Nested states call this method to append directly to the parent's hasher,
        /// avoiding early collapse to 64 bits.
        /// </summary>
        /// <param name="hasher">The XxHash64 hasher to append data to.</param>
        /// <param name="visited">Dictionary mapping visited objects to their reference IDs for back-reference handling.</param>
        public void AppendHashCore(XxHash64 hasher, Dictionary<object, int> visited)
        {
            if (visited.TryGetValue(this, out var refId))
            {
                // Already visited - append backref marker with reference ID
                // This distinguishes which object this is a back-reference to
                AppendLengthPrefixedString(hasher, $"backref:{refId}");
                return;
            }
            visited[this] = visited.Count;  // Assign next reference ID
            AppendFieldHashes(hasher, visited);
        }

        /// <summary>
        /// Appends hash data for this state's fields to the hasher.
        /// Override in derived classes for efficient incremental hashing.
        /// The cycle check is handled by <see cref="AppendHashCore"/> - 
        /// this method only needs to append field data.
        /// </summary>
        /// <param name="hasher">The XxHash64 hasher to append data to.</param>
        /// <param name="visited">Dictionary of already-visited objects, passed to nested states.</param>
        protected virtual void AppendFieldHashes(XxHash64 hasher, Dictionary<object, int> visited)
        {
            // Default implementation: fall back to string-based hash
            AppendLengthPrefixedString(hasher, ToString());
        }

        /// <summary>
        /// Computes a 64-bit hash of the given string using XxHash64.
        /// </summary>
        public static ulong ComputeHash64(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            return XxHash64.HashToUInt64(bytes);
        }

        /// <summary>
        /// Freezes the state, making it immutable. Any attempts to modify a frozen state object
        /// should result in the <see cref="StateFrozenException"/> exception being thrown.
        /// </summary>
        public void Freeze()
        {
            Freeze(visited: new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        public void Freeze(HashSet<object> visited)
        {
            if (visited.Contains(this))
            {
                return;
            }

            visited.Add(this);

            FreezeComponents(visited);

            // Cache fingerprint for mutation validation
            _frozenFingerprint = StringRepresentation();
            IsFrozen = true;
        }

        /// <summary>
        /// Validates that a frozen state has not been mutated since it was frozen.
        /// Throws <see cref="StateFrozenException"/> if mutation is detected.
        /// This check can be disabled by setting <see cref="EnableFreezeValidation"/> to false.
        /// </summary>
        /// <exception cref="StateFrozenException">Thrown if the state was mutated after freezing.</exception>
        public void ValidateNotMutated()
        {
            if (!EnableFreezeValidation)
                return;

            if (!IsFrozen)
                return;

            // Use forceRecompute to bypass caches on this and all nested states
            var currentFingerprint = StringRepresentation(forceRecompute: true);
            if (currentFingerprint != _frozenFingerprint)
            {
                throw new StateFrozenException(
                    $"Frozen state of type '{GetType().Name}' was mutated after freezing. " +
                    $"This indicates a bug - frozen states should not be modified.");
            }
        }

        /// <summary>
        /// Derived state objects must recursively call <see cref="FreezeComponents"/>
        /// on state objects they are composed of.
        /// </summary>
        protected abstract void FreezeComponents(HashSet<object> visited);

        /// <summary>
        /// Obsolete. Override <see cref="FreezeComponents"/> instead.
        /// </summary>
        [Obsolete("Override FreezeComponents instead")]
        protected virtual void LockComponents(HashSet<object> visited)
        {
            FreezeComponents(visited);
        }

        /// <summary>
        /// Derived classes must implement this method to return a unique string
        /// representation of the state they represent. The derived class should call
        /// the <see cref="StringRepresentation(Dictionary{object, string}, string, bool)"/>
        /// method when fetching the string representation of sub-components. They must
        /// pass the objectPaths, path, and forceRecompute parameters to the nested calls.
        /// </summary>
        protected abstract string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path,
            bool forceRecompute);
    }
}
