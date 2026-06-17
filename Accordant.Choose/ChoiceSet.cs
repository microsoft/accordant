// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

/// <summary>
/// This class represents a choice between a set of values for an expression,
/// as well as the _index_ representing the current choice.
/// </summary>
public class ChoiceSet
{
    /// <summary>
    /// The index of the current choice.
    /// </summary>
    public int ValueIndex { get; set; }

    /// <summary>
    /// The set of values that can be chosen.
    /// </summary>
    public object[] Values { get; set; }

    /// <summary>
    /// Clones this object.
    /// </summary>
    public ChoiceSet Clone()
    {
        return new ChoiceSet()
        {
            ValueIndex = ValueIndex,
            Values = Values
        };
    }

    /// <summary>
    /// This method returns the current choice.
    /// </summary>
    public object CurrentChoice()
    {
        return Values[ValueIndex];
    }
}
