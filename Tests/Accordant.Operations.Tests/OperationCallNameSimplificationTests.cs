// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Tests
{
    using Microsoft.Accordant;
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture]
    public class OperationCallNameSimplificationTests
    {
        private NameSimplificationTestSpec spec;
        private Dictionary<string, OperationInput> inputs;

        public OperationCallNameSimplificationTests()
        {
            spec = new NameSimplificationTestSpec();

            inputs = new Dictionary<string, OperationInput>()
            {
                ["Add 1"] = new OperationInput("Add 1", spec.AddOp),
                ["Add 2"] = new OperationInput("Add 2", spec.AddOp),
                ["Count"] = new OperationInput("Count", spec.Count)
            };
        }

        [Test]
        public void EachCallNameSimplified()
        {
            var (callNameMap, _) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                new OperationCall[]
                {
                    ConstructOperationCall("[u] Add 1", "Add 1"),
                    ConstructOperationCall("[j] Add 2", "Add 2"),
                });

            Assert.IsTrue(callNameMap["[u] Add 1"] == "Add 1");
            Assert.IsTrue(callNameMap["[j] Add 2"] == "Add 2");
        }

        [Test]
        public void NoCallNameSimplified()
        {
            var (callNameMap, _) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                new OperationCall[]
                {
                    ConstructOperationCall("[u] Add 1", "Add 1"),
                    ConstructOperationCall("[j] Add 1", "Add 1"),
                });

            Assert.IsTrue(callNameMap["[u] Add 1"] == "[u] Add 1");
            Assert.IsTrue(callNameMap["[j] Add 1"] == "[j] Add 1");
        }

        [Test]
        public void EachCallNameSimplifiedWithDependencies()
        {
            var operationCalls = new List<OperationCall>()
            {
                 ConstructOperationCall("[u] Add 1", "Add 1"),
                 ConstructOperationCall("[j] Add 2", "Add 2")
            };

            operationCalls.Add(new OperationCall(
                "[g] ([u] Add 1 -> Delete)",
                operationInput: new OperationInput(
                    "([u] Add 1 -> Delete)",
                    spec["Delete"],
                    new OperationCall[] { operationCalls[0] })));

            var (callNameMap, operationNameMap) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                operationCalls);

            Assert.IsTrue(callNameMap["[u] Add 1"] == "Add 1");
            Assert.IsTrue(callNameMap["[j] Add 2"] == "Add 2");
            Assert.IsTrue(callNameMap["[g] ([u] Add 1 -> Delete)"] == "(Add 1 -> Delete)");

            Assert.IsTrue(operationNameMap["Add 1"] == "Add 1");
            Assert.IsTrue(operationNameMap["Add 2"] == "Add 2");
            Assert.IsTrue(operationNameMap["([u] Add 1 -> Delete)"] == "(Add 1 -> Delete)");
        }

        [Test]
        public void SomeCallNameSimplifiedWithDependencies()
        {
            var operationCalls = new List<OperationCall>()
            {
                 ConstructOperationCall("[u] Add 1", "Add 1"),
                 ConstructOperationCall("[j] Add 2", "Add 2")
            };

            operationCalls.Add(new OperationCall(
                "[g] ([u] Add 1 -> Delete)",
                operationInput: new OperationInput(
                    "([u] Add 1 -> Delete)",
                    spec["Delete"],
                    new OperationCall[] { operationCalls[0] })));

            operationCalls.Add(new OperationCall(
                "[j] ([u] Add 1 -> Delete)",
                new OperationInput(
                    "([u] Add 1 -> Delete)",
                    spec["Delete"],
                    new OperationCall[] { operationCalls[0] })));

            var (callNameMap, operationNameMap) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                operationCalls);

            Assert.IsTrue(callNameMap["[u] Add 1"] == "Add 1");
            Assert.IsTrue(callNameMap["[j] Add 2"] == "Add 2");
            Assert.IsTrue(callNameMap["[g] ([u] Add 1 -> Delete)"] == "[g] (Add 1 -> Delete)");
            Assert.IsTrue(callNameMap["[j] ([u] Add 1 -> Delete)"] == "[j] (Add 1 -> Delete)");

            Assert.IsTrue(operationNameMap["Add 1"] == "Add 1");
            Assert.IsTrue(operationNameMap["Add 2"] == "Add 2");
            Assert.IsTrue(operationNameMap["([u] Add 1 -> Delete)"] == "(Add 1 -> Delete)");
        }

        [Test]
        public void NoCallNameSimplifiedWithDependencies()
        {
            var operationCalls = new List<OperationCall>()
            {
                 ConstructOperationCall("[u] Add 1", "Add 1"),
                 ConstructOperationCall("[j] Add 1", "Add 1")
            };

            operationCalls.Add(new OperationCall(
                "[g] ([u] Add 1 -> Delete)",
                new OperationInput(
                    "([u] Add 1 -> Delete)",
                    spec["Delete"],
                    new OperationCall[] { operationCalls[0] })));

            operationCalls.Add(new OperationCall(
                "[j] ([u] Add 1 -> Delete)",
                new OperationInput(
                    "([u] Add 1 -> Delete)",
                    spec["Delete"],
                    new OperationCall[] { operationCalls[0] })));

            var (callNameMap, operationNameMap) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                operationCalls);

            Assert.IsTrue(callNameMap["[u] Add 1"] == "[u] Add 1");
            Assert.IsTrue(callNameMap["[j] Add 1"] == "[j] Add 1");
            Assert.IsTrue(callNameMap["[g] ([u] Add 1 -> Delete)"] == "[g] ([u] Add 1 -> Delete)");
            Assert.IsTrue(callNameMap["[j] ([u] Add 1 -> Delete)"] == "[j] ([u] Add 1 -> Delete)");

            Assert.IsTrue(operationNameMap["Add 1"] == operationNameMap["Add 1"]);
            Assert.IsTrue(operationNameMap["([u] Add 1 -> Delete)"] == operationNameMap["([u] Add 1 -> Delete)"]);
        }

        [Test]
        public void ComplexLabelMultipleCallNamesNotSimplified()
        {
            var (callNameMap, _) = TestCaseGenerator.ConstructSimplifiedOperationCallNameMap(
                spec,
                new OperationCall[]
                {
                    ConstructOperationCall("[u-0] Add 1", "Add 1"),
                    ConstructOperationCall("[u-1] Add 1", "Add 1"),
                });

            Assert.IsTrue(callNameMap["[u-0] Add 1"] == "[u-0] Add 1");
            Assert.IsTrue(callNameMap["[u-1] Add 1"] == "[u-1] Add 1");
        }

        private OperationCall ConstructOperationCall(string name, string operationName) =>
                new OperationCall(name, inputs[operationName]);
    }

    #region Test Helper Classes

    /// <summary>
    /// Minimal spec for name simplification tests - operations don't need real Apply/Execute logic.
    /// </summary>
    public class NameSimplificationTestSpec : Spec<AtomicState<int>>
    {
        public StubOperation AddOp { get; } = new("Add");
        public StubOperation Count { get; } = new("Count");
        public StubOperationWithDerivation Delete { get; }

        public NameSimplificationTestSpec()
        {
            Delete = new StubOperationWithDerivation("Delete", "Add");
            
            this["Add"] = AddOp;
            this["Count"] = Count;
            this["Delete"] = Delete;
        }
    }

    /// <summary>
    /// Minimal stub operation for tests that only need operation identity, not behavior.
    /// </summary>
    public class StubOperation : Operation<object, object, AtomicState<int>>
    {
        public StubOperation(string name) : base(name) { }

        public override ExpectedOutcomes Apply(object request, AtomicState<int> state)
        {
            return new ExpectedOutcome(
                ResponseValidator.FromPredicate<object>(r => true),
                state);
        }
    }

    /// <summary>
    /// Stub operation with a derivation for testing derived request handling.
    /// </summary>
    public class StubOperationWithDerivation : Operation<object, object, AtomicState<int>>
    {
        private readonly IReadOnlyList<RequestDerivation> _derivations;

        public StubOperationWithDerivation(string name, string fromOperation) : base(name)
        {
            _derivations = new List<RequestDerivation>
            {
                Derive.From(fromOperation).As((object request, object response) => (object)null)
            };
        }

        public override IReadOnlyList<RequestDerivation> DerivedFrom => _derivations;

        public override ExpectedOutcomes Apply(object request, AtomicState<int> state)
        {
            return new ExpectedOutcome(
                ResponseValidator.FromPredicate<object>(r => true),
                state);
        }
    }

    #endregion
}
