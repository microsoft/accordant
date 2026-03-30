// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace StateSamples.StateDefinition
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;

    /// <summary>
    /// This class denotes a state each of whose properties is an
    /// "atomic" state. The generated AddressState class (in State.g.cs)
    /// exposes the properties with the same type, wrapping and unwrapping
    /// them in an <see cref="AtomicState{T}"/> object.
    /// </summary>
    [StateDefinition]
    public class AddressState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Line1 { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Line2 { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string State { get; set; }
    }

    /// <summary>
    /// This class denotes a state, with two properties that are "atomic"
    /// and one reference to another state object (AddressState). The generated
    /// PersonState class (in State.g.cs) exposes properties with the same type
    /// for all three of these properties in this class.
    /// </summary>
    [StateDefinition]
    public class PersonState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public AddressState Address { get; set; }
    }

    /// <summary>
    /// This class denotes a state one of whose property is an atomic state
    /// while the other is a list of PersonState objects. The generated
    /// CompanyState class (in State.g.c) exposes a property with the same type
    /// for name while the type of Employees changes to ListState<PersonState>.
    /// </summary>
    [StateDefinition]
    public class CompanyState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// This translates to a property the same name of type ListState<PersonState>.
        /// Even though the list type changes from List to ListState, ListState has a lot
        /// of the same methods as List.
        /// </summary>
        public List<PersonState> Employees { get; set; }
    }

    /// <summary>
    /// This class denotes a state with atomic as well as more complex types.
    /// </summary>
    [StateDefinition]
    public class AccountState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// This translates to a property with the same name but ListAtomicState<string> type.
        /// Since this is a list of a primitive type, it translates to ListAtomicState instead
        /// of ListState. ListAtomicState<string> has a lot of the same methods as List<string>.
        /// </summary>
        public List<string> Tags { get; set; }

        /// <summary>
        /// This translates to a property with the same name but MapState<ImageState> type.
        /// The key of MapState is always a string which is why it's not explicitly mentioned
        /// in its type signature. MapState<ImageState> has a lot of the same methods as
        /// Dictionary<string, ImageState>.
        /// </summary>
        public Dictionary<string, ImageState> Images { get; set; }

        /// <summary>
        /// This translates to a property with the same name but MapAtomicState<string> type.
        /// The key of MapAtomicState is always a string which is why it's not explicitly mentioned
        /// in its type signature. Since the value of the dictionary is a primitive type, it is thus 
        /// mapped to MapAtomicState instead of MapState. MapAtomicState<string> has a lot of the same
        /// methods as Dictionary<string, string>.
        /// </summary>
        public Dictionary<string, string> CustomerProperties { get; set; }
    }

    [StateDefinition]
    public class ImageState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// Even though Lists typically translate to ListState, we treat this
        /// property as an "atomic" value through the use of <see cref="AtomicAttribute"/>
        /// attribute. We must give the fully qualified name of a static method which can
        /// be given this value to return its unique string representation. This string
        /// representation is used when computing state hashes and thus different values
        /// of this property must return different string representations.
        /// </summary>
        [Atomic("StateSamples.StateDefinition.ImageState.ContentToString")]
        public List<byte> Content { get; set; }

        public static string ContentToString(List<byte> content)
        {
            return content == null ?
                "<null>" :
                string.Join(string.Empty, content.Select(b => b.ToString("x2")));
        }
    }

    /// <summary>
    /// This class defines state that's further extended by two different
    /// derived classes.
    /// </summary>
    [StateDefinition]
    public class VehicleState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Make { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public DateTime Year { get; set; }
    }

    /// <summary>
    /// This state class derives from a base state class and adds
    /// additional properties.
    /// </summary>
    [StateDefinition]
    public class CarState : VehicleState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public int TrunkSize { get; set; }
    }

    /// <summary>
    /// This state class derives from a base state class and adds
    /// additional properties.
    /// </summary>
    [StateDefinition]
    public class MotorcycleState : VehicleState
    {
        /// <summary>
        /// This translates to a property with the same type and name.
        /// </summary>
        public bool HasSideCar { get; set; }
    }
}
