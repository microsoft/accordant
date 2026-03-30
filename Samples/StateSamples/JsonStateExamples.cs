// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace StateSamples.JsonStateExamples
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Accordant;

    /// <summary>
    /// This file demonstrates the JsonState approach to defining state classes.
    /// 
    /// Compare this to StateDefinition.cs which uses the [StateDefinition] attribute
    /// and code generation. Both approaches are valid, but JsonState is simpler:
    /// 
    /// [StateDefinition] approach (StateDefinition.cs):
    /// - Requires [StateDefinition] attribute on classes
    /// - Requires running the StateSourceCodeGenerator tool to generate State.g.cs
    /// - Generated classes use AtomicState, ListState, MapState wrappers
    /// - More verbose but provides fine-grained control over state tracking
    /// 
    /// JsonState approach (this file):
    /// - Simply inherit from JsonState
    /// - No code generation needed
    /// - Use standard C# types (List, Dictionary, etc.)
    /// - Cloning, fingerprinting, and locking work automatically via JSON serialization
    /// - Simpler to write and understand
    /// 
    /// When to use JsonState:
    /// - Prototyping and quick iterations
    /// - When simplicity is more important than fine-grained control
    /// - When you want to avoid the code generation step
    /// 
    /// When to use [StateDefinition]:
    /// - When you need explicit control over what's atomic vs nested
    /// - When you have complex serialization requirements (e.g., [Atomic] attribute)
    /// </summary>
    public static class JsonStateDocumentation { }

    // ============================================================================
    // Simple State Examples
    // ============================================================================

    /// <summary>
    /// A simple address state using JsonState.
    /// Compare to AddressState in StateDefinition.cs.
    /// 
    /// Note: With JsonState, you just define properties normally.
    /// No special attributes or code generation required.
    /// </summary>
    public class AddressJsonState : JsonState
    {
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
    }

    /// <summary>
    /// A person state with a nested address.
    /// Compare to PersonState in StateDefinition.cs.
    /// 
    /// With JsonState, nested state objects are just normal properties.
    /// Cloning and locking automatically handle the nested object.
    /// </summary>
    public class PersonJsonState : JsonState
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public AddressJsonState Address { get; set; }
    }

    // ============================================================================
    // Collection Examples
    // ============================================================================

    /// <summary>
    /// A company state with a list of employees.
    /// Compare to CompanyState in StateDefinition.cs.
    /// 
    /// With JsonState, just use List&lt;T&gt; directly - no ListState wrapper needed.
    /// </summary>
    public class CompanyJsonState : JsonState
    {
        public string Name { get; set; }
        public List<PersonJsonState> Employees { get; set; }
    }

    /// <summary>
    /// An account state with various collection types.
    /// Compare to AccountState in StateDefinition.cs.
    /// 
    /// JsonState handles all standard collection types:
    /// - List&lt;T&gt; for primitive lists
    /// - List&lt;TState&gt; for lists of state objects
    /// - Dictionary&lt;string, T&gt; for maps
    /// </summary>
    public class AccountJsonState : JsonState
    {
        public string Name { get; set; }

        /// <summary>
        /// A list of primitive strings - just use List&lt;string&gt; directly.
        /// </summary>
        public List<string> Tags { get; set; }

        /// <summary>
        /// A dictionary of nested state objects.
        /// </summary>
        public Dictionary<string, ImageJsonState> Images { get; set; }

        /// <summary>
        /// A dictionary of primitive values.
        /// </summary>
        public Dictionary<string, string> CustomerProperties { get; set; }
    }

    /// <summary>
    /// An image state demonstrating binary data handling.
    /// Compare to ImageState in StateDefinition.cs.
    /// 
    /// Note: Unlike [StateDefinition] which requires [Atomic] attribute for
    /// special serialization, JsonState handles byte arrays naturally.
    /// </summary>
    public class ImageJsonState : JsonState
    {
        public string Name { get; set; }
        public string ContentType { get; set; }

        /// <summary>
        /// Binary content as a byte array.
        /// JsonState serializes this as a base64 string automatically.
        /// </summary>
        public byte[] Content { get; set; }
    }

    // ============================================================================
    // Inheritance Examples
    // ============================================================================

    /// <summary>
    /// A base vehicle state.
    /// Compare to VehicleState in StateDefinition.cs.
    /// 
    /// JsonState supports inheritance naturally.
    /// </summary>
    public class VehicleJsonState : JsonState
    {
        public string Make { get; set; }
        public string Model { get; set; }
        public DateTime Year { get; set; }
    }

    /// <summary>
    /// A car state that extends vehicle.
    /// Compare to CarState in StateDefinition.cs.
    /// </summary>
    public class CarJsonState : VehicleJsonState
    {
        public int TrunkSize { get; set; }
    }

    /// <summary>
    /// A motorcycle state that extends vehicle.
    /// Compare to MotorcycleState in StateDefinition.cs.
    /// </summary>
    public class MotorcycleJsonState : VehicleJsonState
    {
        public bool HasSideCar { get; set; }
    }

    // ============================================================================
    // Advanced Examples (JsonState-specific features)
    // ============================================================================

    /// <summary>
    /// Demonstrates cyclic references, which JsonState handles automatically.
    /// This would be complex to implement with [StateDefinition].
    /// </summary>
    public class EmployeeJsonState : JsonState
    {
        public string Name { get; set; }
        public string Title { get; set; }

        /// <summary>
        /// Reference to manager - can be null or point to another employee.
        /// JsonState preserves object identity, so if two employees share
        /// the same manager, the cloned state will also share references.
        /// </summary>
        public EmployeeJsonState Manager { get; set; }

        /// <summary>
        /// Direct reports - can include cyclic references back to this employee.
        /// </summary>
        public List<EmployeeJsonState> DirectReports { get; set; }
    }

    /// <summary>
    /// Demonstrates a graph structure with cycles.
    /// JsonState handles this automatically via reference preservation.
    /// </summary>
    public class GraphNodeJsonState : JsonState
    {
        public string Id { get; set; }
        public string Label { get; set; }

        /// <summary>
        /// Neighbors can include cycles (e.g., A -> B -> A).
        /// JsonState serializes these correctly using $ref/$id markers.
        /// </summary>
        public List<GraphNodeJsonState> Neighbors { get; set; }
    }
}
