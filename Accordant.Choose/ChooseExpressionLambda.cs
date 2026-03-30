// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A choose expression lambda is a lambda that contains zero or more
    /// choose expressions. The Run method of this class can be used to run
    /// such a lambda. The lambda is invoked multiple times such that all combinations
    /// of all choose expressions are exercised.
    /// </summary>
    public class ChooseExpressionLambda
    {
        internal static ChooseExpressionLambdaConfig CurrentConfig { get; set; }

        /// <summary>
        /// Runs a choose expression lambda, enough times such that all
        /// combinations of choices of all its multi-valued expressions are chosen.
        /// </summary>
        public static void Run(Action lambda)
        {
            var previousConfig = CurrentConfig;

            try
            {
                CurrentConfig = new ChooseExpressionLambdaConfig();
                RunInternal(lambda);
            }
            finally
            {
                CurrentConfig = previousConfig;
            }
        }

        /// <summary>
        /// Runs a choose expression lambda that returns a response of type
        /// TResponse. Since the lambda can be invoked multiple times, this method
        /// returns an enumeration of the responses that result from calling it each time.
        /// </summary>
        public static IEnumerable<TResponse> Run<TResponse>(Func<TResponse> lambda)
        {
            var results = new List<TResponse>();

            Run(() =>
            {
                var response = lambda();
                results.Add(response);
            });

            return results;
        }

        private static void RunInternal(Action lambda)
        {
            while (true)
            {
                CurrentConfig.ChoiceSetIndex = 0;

                lambda();

                if (CurrentConfig.ChoiceSpaceCoordinate.Count == 0 ||
                    CurrentConfig.ChoiceSpaceCoordinate[0].ValueIndex >= CurrentConfig.ChoiceSpaceCoordinate[0].Values.Length)
                {
                    break;
                }
            }
        }
    }
}
