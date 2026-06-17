// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant;

using System.Collections.Generic;

internal class ChooseExpressionLambdaConfig
{
    internal List<ChoiceSet> ChoiceSpaceCoordinate { get; set; } = null;

    internal int ChoiceSetIndex { get; set; } = 0;

    internal List<ChoiceSet> LastKnownChoiceSpaceCoordinate { get; set; } = new List<ChoiceSet>();

    internal ChooseExpressionLambdaConfig()
    {
        ChoiceSpaceCoordinate = LastKnownChoiceSpaceCoordinate;
    }

    internal object CurrentChoice()
    {
        var choiceSet = ChoiceSpaceCoordinate[ChoiceSetIndex];
        return choiceSet.CurrentChoice();
    }
}
