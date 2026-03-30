// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Accordant;
    using NUnit.Framework;

    [TestFixture]
    public class ChooseTests
    {
        [Test]
        public static void SimpleTest()
        {
            var results = ChooseExpressionLambda.Run(() =>
            {
                if (!RandomChoice())
                {
                    return -1;
                }

                if (!RandomChoice())
                {
                    return -2;
                }

                if (!RandomChoice())
                {
                    return -3;
                }

                static bool RandomChoice()
                {
                    return Choose.Each(true, false);
                }

                return 1;
            });

            Assert.IsTrue(results.SequenceEqual([1, -3, -2, -1]));
        }

        [Test]
        public static void DifferentNumberOfChoicesInBranches()
        {
            var results = ChooseExpressionLambda.Run(() =>
            {
                int i = Choose.Each(1, 2);

                if (i <= 1)
                {
                    return 1;
                }
                else
                {
                    int j = Choose.Each(4, 5);

                    if (j == 4)
                    {
                        int k = Choose.Each(6);
                        return 2;
                    }
                    else
                    {
                        int l = Choose.Each(8);
                        return 3;
                    }
                }
            });

            Assert.IsTrue(results.SequenceEqual([1, 2, 3]));
        }

        [Test]
        public static void SortSymbolicArrayUsingCustomSortingMethod()
        {
            var results = new List<int?[]>();

            ChooseExpressionLambda.Run(() =>
            {
                var array = new int?[] { 3, null, 2, 9 };

                int highestLowValue = int.MinValue;
                int lowestHighValue = int.MaxValue;

                var comparator = new ConstraintRespectingComparator(lowestHighValue, highestLowValue); ;

                BubbleSort(
                    array,
                    (left, right) => comparator.Compare(left, right));

                results.Add(array);
            });

            Assert.IsTrue(results.Count() == 4);

            void AssertResultsContain(IList<int?> target)
            {
                Assert.IsTrue(results.Any(r => r.SequenceEqual(target)));
            }

            AssertResultsContain([null, 2, 3, 9]);
            AssertResultsContain([2, null, 3, 9]);
            AssertResultsContain([2, 3, null, 9]);
            AssertResultsContain([2, 3, 9, null]);
        }

        [Test]
        public static void SortSymbolicArrayUsingStandardOrderByMethod()
        {
            var results = ChooseExpressionLambda.Run(() =>
            {
                var array = new int?[] { 3, null, 2, 9 };

                int highestLowValue = int.MinValue;
                int lowestHighValue = int.MaxValue;

                array = array
                    .OrderBy(
                        e => e,
                        new ConstraintRespectingComparator(lowestHighValue, highestLowValue))
                    .ToArray();

                return array;
            });

            Assert.IsTrue(results.Count() == 4);

            void AssertResultsContain(IList<int?> target)
            {
                Assert.IsTrue(results.Any(r => r.SequenceEqual(target)));
            }

            AssertResultsContain([null, 2, 3, 9]);
            AssertResultsContain([2, null, 3, 9]);
            AssertResultsContain([2, 3, null, 9]);
            AssertResultsContain([2, 3, 9, null]);
        }

        public class ConstraintRespectingComparator : IComparer<int?>
        {
            private int lowestHighValue;
            private int highestLowValue;

            public ConstraintRespectingComparator(int lowestHighValue, int highestLowValue)
            {
                this.lowestHighValue = lowestHighValue;
                this.highestLowValue = highestLowValue;
            }

            public int Compare(int? left, int? right)
            {
                if (left == right)
                {
                    return 0;
                }

                if (left != null && right != null)
                {
                    return left > right ? 1 : -1;
                }

                if (left == null && right >= lowestHighValue ||
                    right == null && left <= highestLowValue)
                {
                    return -1;
                }

                if (left == null && right <= highestLowValue ||
                    right == null && left >= lowestHighValue)
                {
                    return 1;
                }

                bool isLeftGreaterThanRight = Choose.Each(true, false);

                highestLowValue = isLeftGreaterThanRight ?
                    left == null ? Math.Max(highestLowValue, (int)right) : highestLowValue :
                    left == null ? highestLowValue : Math.Max(highestLowValue, (int)left);

                lowestHighValue = isLeftGreaterThanRight ?
                    left == null ? lowestHighValue : Math.Min(lowestHighValue, (int)left) :
                    left == null ? Math.Min(lowestHighValue, (int)right) : lowestHighValue;

                return isLeftGreaterThanRight ? 1 : -1;
            }
        }

        private static void BubbleSort(int?[] array, Func<int?, int?, int> leftGreaterThanRight)
        {
            int n = array.Length;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < n - i - 1; j++)
                {
                    if (leftGreaterThanRight(array[j], array[j + 1]) == 1)
                    {
                        int? temp = array[j];
                        array[j] = array[j + 1];
                        array[j + 1] = temp;
                    }
                }
            }
        }
    }
}
