// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections;
using System.Collections.Generic;

/// <summary>
/// This class represents a set of operation inputs. Each operation input
/// has a unique name and two inputs cannot share the same
/// name.
/// </summary>
public class InputSet : IEnumerable<OperationInput>
{
    private Dictionary<string, OperationInput> inputDict =
        new Dictionary<string, OperationInput>();

    /// <summary>
    /// List of operation inputs in this set.
    /// </summary>
    public IEnumerable<OperationInput> Inputs
    {
        get => inputDict.Values;
    }

    /// <summary>
    /// The indexer property can be used to get the operation input given its name.
    /// </summary>
    public OperationInput this[string name]
    {
        get => inputDict[name];
    }

    /// <summary>
    /// This method adds an operation input to this set.
    /// </summary>
    public void Add(OperationInput input)
    {
        if (inputDict.ContainsKey(input.Name))
        {
            throw new InputSetException(
                $"Encountered two operation inputs with the same name '{input.Name}'");
        }

        inputDict[input.Name] = input;
    }

    /// <summary>
    /// This method retrieves the operation input given its name.
    /// </summary>
    public OperationInput GetInput(string name)
    {
        return inputDict[name];
    }

    /// <summary>
    /// This method indicates whether an operation input with the given name
    /// exists in this set.
    /// </summary>
    public bool ContainsInput(string name)
    {
        return inputDict.ContainsKey(name);
    }

    public IEnumerator<OperationInput> GetEnumerator()
    {
        return inputDict.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return inputDict.Values.GetEnumerator();
    }
}
