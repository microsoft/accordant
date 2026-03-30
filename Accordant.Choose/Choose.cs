// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// This class represents a choose expression that can choose one or
    /// more values. A lambda that contains choose expressions is evaluated
    /// multiple times such that each possible choice for each choose expression
    /// and all their combinations are exercised.
    /// </summary>
    public class Choose
    {
        /// <summary>
        /// Choose each value from a set of values. Each value will be returned to the caller
        /// each time the method is invoked through the ChooseExpressionLambda.Run
        /// method.
        /// </summary>
        public static T Each<T>(params T[] values)
        {
            return Each((IList<T>)values);
        }

        /// <summary>
        /// Choose each value from a set of values. Each value will be returned to the caller
        /// each time the method is invoked through the ChooseExpressionLambda.Run
        /// method.
        /// </summary>
        public static T Each<T>(IList<T> values)
        {

            var config = ChooseExpressionLambda.CurrentConfig;

            if (config.ChoiceSetIndex >= config.ChoiceSpaceCoordinate.Count)
            {
                var newChoiceSet = new ChoiceSet()
                {
                    ValueIndex = 0,
                    Values = values.Cast<object>().ToArray()
                };

                config.ChoiceSpaceCoordinate = config.LastKnownChoiceSpaceCoordinate;
                config.ChoiceSpaceCoordinate.Add(newChoiceSet);
            }

            var returnValue = (T)config.CurrentChoice();

            if ((config.ChoiceSetIndex + 1) == config.ChoiceSpaceCoordinate.Count)
            {
                config.LastKnownChoiceSpaceCoordinate = config
                    .ChoiceSpaceCoordinate
                    .Select(c => c.Clone()).ToList();

                AdvanceChoiceSpaceCoordinate();
            }

            config.ChoiceSetIndex++;

            return returnValue;
        }

        private static void AdvanceChoiceSpaceCoordinate()
        {
            var config = ChooseExpressionLambda.CurrentConfig;
            var choiceSet = config.ChoiceSpaceCoordinate[config.ChoiceSetIndex];

            var currentChoiceSet = choiceSet;

            while (true)
            {
                if (currentChoiceSet == null)
                {
                    break;
                }

                currentChoiceSet.ValueIndex++;

                if (currentChoiceSet.ValueIndex == currentChoiceSet.Values.Length)
                {
                    config.ChoiceSpaceCoordinate.RemoveAt(config.ChoiceSpaceCoordinate.Count - 1);

                    currentChoiceSet = config.ChoiceSpaceCoordinate.Count == 0 ?
                        null :
                        config.ChoiceSpaceCoordinate[config.ChoiceSpaceCoordinate.Count - 1];
                }
                else
                {
                    break;
                }
            }
        }
    }
}
