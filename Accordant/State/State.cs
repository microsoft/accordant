// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
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
    public abstract class State
    {
        public static Random Random { get; } = new Random();

        private static SHA256 SHA256 = SHA256.Create();

        protected string stringRepresentation = null;
        protected string stateHash = null;

        /// <summary>
        /// Indicates whether a state object is locked for modification or not.
        /// State objects are not locked when initially created so the user
        /// can modify the state in an 'imperative' fashion. They are then locked
        /// by the framework once returned from the method that created them so they
        /// are effectively immutable. Once locked, the only way to modify a state object
        /// is to clone it to create a new state object that can be modified till it's also
        /// eventually locked.
        /// </summary>
        [JsonIgnore]
        public bool Locked { get; protected set; } = false;

        /// <summary>
        /// Clones the state object. The cloned state object is initially not locked.
        /// </summary>
        /// <returns></returns>
        public virtual State Clone()
        {
            var (clone, _) = CloneWithMap();
            return clone;
        }

        /// <summary>
        /// Clones the state object. The cloned state object is initially not locked.
        /// This also returns a map that maps objects in the original state to corresponding
        /// objects in the cloned state.
        /// </summary>
        public (State, Dictionary<object, object>) CloneWithMap()
        {
            var clonedMap = new Dictionary<object, object>();
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

            return (State)clonedMap[this];
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
        public string StringRepresentation()
        {
            var computeStringRepresentation = !Locked || stringRepresentation == null;

            if (computeStringRepresentation)
            {
                var result = StringRepresentation(
                    objectPaths: new Dictionary<object, string>(),
                    path: "$");
                Invariant.Assert(result != null);

                if (!Locked)
                {
                    return result;
                }

                stringRepresentation = result;
            }

            return stringRepresentation;
        }

        /// <summary>
        /// See <see cref="StringRepresentation()"/> for details. This method should not be
        /// directly called by users and only by other classes that inherit from the
        /// <see cref="State"/> class.
        /// </summary>
        public string StringRepresentation(
            Dictionary<object, string> objectPaths,
            string path)
        {
            if (objectPaths.ContainsKey(this))
            {
                return objectPaths[this];
            }

            objectPaths[this] = path;

            return StringRepresentationInternal(objectPaths, path);
        }

        /// <summary>
        /// Returns the state hash, computed over the string representation of the state.
        /// </summary>
        public string GetStateHash()
        {
            if (!Locked)
            {
                var stringRepresentation = ToString();
                return ComputeStringHash(stringRepresentation);
            }
            else
            {
                if (stateHash == null)
                {
                    var stringRepresentation = ToString();
                    stateHash = ComputeStringHash(stringRepresentation);
                }
            }

            return stateHash;
        }

        /// <summary>
        /// This method computes the hash of the given string.
        /// </summary>
        public static string ComputeStringHash(string str)
        {
            var hashBytes = SHA256.ComputeHash(Encoding.UTF8.GetBytes(str));

            return string.Join(
                string.Empty,
                hashBytes.Select(b => b.ToString("x2")));

        }

        /// <summary>
        /// Marks the state as locked. Any attempts to modify a locked state object
        /// should result in the <see cref="StateLockedException"/> exception being thrown.
        /// </summary>
        public void Lock()
        {
            Lock(visited: new HashSet<object>());
        }

        public void Lock(HashSet<object> visited)
        {
            if (visited.Contains(this))
            {
                return;
            }

            visited.Add(this);

            LockComponents(visited);

            Locked = true;
        }

        /// <summary>
        /// Derived state objects must recursively call <see cref="LockComponents"/>
        /// on state objects they are composed of.
        /// </summary>
        protected abstract void LockComponents(HashSet<object> visited);

        /// <summary>
        /// Derived classes must implement this method to return a unique string
        /// representation of the state they represent. The derived class should call
        /// the <see cref="StringRepresentation(Dictionary{object, string}, string)"/>
        /// method when fetching the string representation of sub-components. They must
        /// pass the objectPaths and path parameters to the nested StringRepresentation calls.
        /// </summary>
        protected abstract string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path);
    }
}
