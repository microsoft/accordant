# State Samples

This project showcases different approaches to defining state classes in Accordant.

## Two Approaches to Defining State

### 1. JsonState (Simple Approach) - See `JsonStateExamples.cs`

The simplest way to define state is to inherit from `JsonState`:

```csharp
public class PersonJsonState : JsonState
{
    public string Name { get; set; }
    public int Age { get; set; }
    public AddressJsonState Address { get; set; }
}
```

**Pros:**
- No code generation required
- Use standard C# types (List, Dictionary, etc.)
- Handles cyclic references automatically
- Simpler to write and understand

**Cons:**
- Less fine-grained control over serialization
- Slightly more overhead due to JSON serialization

### 2. [StateDefinition] + Code Generation - See `StateDefinition.cs` and `State.g.cs`

The original approach uses `[StateDefinition]` attribute and code generation:

```csharp
[StateDefinition]
public class PersonState
{
    public string Name { get; set; }
    public int Age { get; set; }
    public AddressState Address { get; set; }
}
```

**Pros:**
- Fine-grained control (AtomicState, ListState, MapState wrappers)
- Custom serialization via `[Atomic]` attribute
- More efficient for complex state tracking

**Cons:**
- Requires running StateSourceCodeGenerator tool
- More verbose generated code

---

## Using the [StateDefinition] Approach

State definitions cannot directly be used in the specs as they don't inherit from the
State class. They can however be converted to corresponding classes that do inherit from
the State class and implement all the required methods through the Accordant.StateCodeGenerator
tool. The translation is fairly boilerplate which is why it's recommended to declare the
state definition using POCOs (plain old C# objects) and use automated tooling to generate
the state classes to be used in the specs.

### How to install the state source code generator tool

You can install the `StateSourceCodeGenerator` tool executable through the following command:

```
dotnet tool install -g Accordant.StateSourceCodeGenerator
```

This will install the `StateSourceCodeGenerator.exe` tool in the global dotnet tool folder so it
can be invoked from any location.

### How to invoke the state source code generator tool

You invoke the `StateSourceCodeGenerator` tool executable as follows:

```
StateSourceCodeGenerator -i .\bin\Debug\net6.0\StateSamples.dll -n StateSamples -o State.g.cs 
```

The source code generator takes three arguments:

1. The path to the assembly containing the state definition classes (annotated with `StateDefintion` attribute)
2. The namespace in which to enclose the generated state classes. Since the state definition classes and generated classes
   have the same name, we follow a convention where the state definition classes are defined in the namespace \<Namespace\>.StateDefinition
   while the generated classes are enclosed in just \<Namespace\>.
3. The path to the file where the generated classes are written to.