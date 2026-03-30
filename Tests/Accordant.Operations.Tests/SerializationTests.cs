// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using Microsoft.Accordant;
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using static Accordant.Tests.OperationTests;

    /// <summary>
    /// Helper class for value comparison (simplified version that handles BlogPost objects).
    /// </summary>
    internal static class TypeHelper
    {
        public static bool AreValuesEqual(object v1, object v2)
        {
            if (v1 == null && v2 == null) return true;
            if (v1 == null || v2 == null) return false;
            if (v1.GetType() != v2.GetType()) return false;
            
            // For primitive types and strings, use Equals
            var type = v1.GetType();
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            {
                return v1.Equals(v2);
            }
            
            // For complex objects, compare fields/properties using reflection
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var val1 = prop.GetValue(v1);
                var val2 = prop.GetValue(v2);
                if (!AreValuesEqual(val1, val2))
                {
                    return false;
                }
            }
            
            return true;
        }
    }

    [TestFixture]
    public class SerializationTests
    {
        [Test]
        public void SerializationTest()
        {
            var spec = new BlogPostServiceSpec();

            var operations = new InputSet()
            {
                spec.AddOp.With(new BlogPost()
                {
                    Name = "Hello",
                    Content = "World"
                })
            };

            var startingState = new DictionaryState<DictionaryState<AtomicState<string>>>();

            var seqTestCases = spec.GenerateTests(
                startingState,
                operations,
                new TestGenerationOptions()
                {
                    MaxDepth = 5
                });

            var context = spec.CreateTestingContext();

            var deserializedSeqTestCases = TestCaseGenerator.DeserializeSequentialTestCases(
                context,
                TestCaseGenerator.SerializeSequentialTestCases(
                    context,
                    seqTestCases));

            Assert.IsTrue(AreListsSame(
                seqTestCases,
                deserializedSeqTestCases,
                AreSequentialTestCaseSame));

            var concurrentTestCases = spec.GenerateConcurrentTests(
                startingState,
                operations,
                new TestGenerationOptions()
                {
                    MaxDepth = 5
                });

            var deserializedConcurrentTestCases = TestCaseGenerator.DeserializeConcurrentTestCases(
                context,
                TestCaseGenerator.SerializeConcurrentTestCases(
                    context,
                    concurrentTestCases));

            Assert.IsTrue(AreListsSame(
                concurrentTestCases,
                deserializedConcurrentTestCases,
                AreConcurrentTestCaseSame));
        }

        private static bool AreListsSame<T>(
            IList<T> list1,
            IList<T> list2,
            Func<T, T, bool> elementComparer)
        {
            if (list1 == null)
            {
                return list2 == null;
            }

            if (list2 == null)
            {
                return list1 == null;
            }

            if (list1.Count != list2.Count)
            {
                return false;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                var e1 = list1[i];
                var e2 = list2[i];

                if (!elementComparer(e1, e2))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreConcurrentTestCaseSame(
            ConcurrentTestCase tc1,
            ConcurrentTestCase tc2)
        {
            if (tc1 == null)
            {
                return tc2 == null;
            }

            if (tc2 == null)
            {
                return tc1 == null;
            }

            return
                tc1.Description == tc2.Description &&
                tc1.Comments == tc2.Comments &&
                AreListsSame(
                    tc1.SequentialOperationCalls,
                    tc2.SequentialOperationCalls,
                    AreOperationCallsSame) &&
                AreListsSame(
                    tc1.ConcurrentOperationCalls,
                    tc2.ConcurrentOperationCalls,
                    AreOperationCallsSame);
        }

        private static bool AreSequentialTestCaseSame(
            SequentialTestCase tc1,
            SequentialTestCase tc2)
        {
            if (tc1 == null)
            {
                return tc2 == null;
            }

            if (tc2 == null)
            {
                return tc1 == null;
            }

            return
                tc1.Description == tc2.Description &&
                tc1.Comments == tc2.Comments &&
                AreListsSame(
                    tc1.OperationCalls,
                    tc2.OperationCalls,
                    AreOperationCallsSame);
        }

        private static bool AreOperationCallsSame(OperationCall e1, OperationCall e2)
        {
            if (e1 == null)
            {
                return e2 == null;
            }

            if (e2 == null)
            {
                return e1 == null;
            }

            return
                e1.Name == e2.Name &&
                AreInputsSame(e1.OperationInput, e2.OperationInput);
        }

        private static bool AreInputsSame(OperationInput e1, OperationInput e2)
        {
            if (e1 == null)
            {
                return e2 == null;
            }

            if (e2 == null)
            {
                return e1 == null;
            }

            return
                e1.Name == e2.Name &&
                ArePollingSetupsSame(e1.Polling, e2.Polling) &&
                e1.SkipPolling == e2.SkipPolling &&
                e1.Operation == e2.Operation &&
                AreListsSame(
                    e1.DerivedFromOperationCalls,
                    e2.DerivedFromOperationCalls,
                    AreOperationCallsSame) &&
                TypeHelper.AreValuesEqual(e1.Request, e2.Request);
        }

        private static bool ArePollingSetupsSame(
            PollingSetup p1,
            PollingSetup p2)
        {
            if (p1 == null)
            {
                return p2 == null;
            }

            if (p2 == null)
            {
                return p1 == null;
            }

            return
                p1.Operation == p2.Operation &&
                p1.Variant == p2.Variant &&
                p1.MaxRetryCount == p2.MaxRetryCount &&
                p1.WaitTimeInMs == p2.WaitTimeInMs;
        }
    }
}
