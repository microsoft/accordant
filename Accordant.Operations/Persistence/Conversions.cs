// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// This class contains methods that convert to/from the file layout classes
    /// and in-memory objects representing test cases.
    /// </summary>
    public static class Conversions
    {
        public static SequentialTestCase ToSequentialTestCase(
            this SequentialTestCaseFileRecord testCaseFileRecord,
            ISpec spec)
        {
            var operationCalls = new List<OperationCall>();
            var previousOperationCalls = new Dictionary<string, OperationCall>();

            foreach (var callFileRecord in testCaseFileRecord.OperationCalls)
            {
                var operationCall = callFileRecord.ToOperationCall(
                    spec,
                    previousOperationCalls);

                operationCalls.Add(operationCall);
                previousOperationCalls[operationCall.Name] = operationCall;
            }

            return new SequentialTestCase()
            {
                Description = testCaseFileRecord.Description,
                Comments = testCaseFileRecord.Comments,
                OperationCalls = operationCalls
            };
        }

        public static SequentialTestCaseFileRecord ToSequentialTestCaseFileRecord(
            this SequentialTestCase testCase,
            ISpec spec)
        {
            return new SequentialTestCaseFileRecord()
            {
                Description = testCase.Description,
                Comments = testCase.Comments,
                OperationCalls = testCase
                    .OperationCalls
                    .Select(call => call.ToOperationCallFileRecord(spec)).ToList()
            };
        }

        public static ConcurrentTestCase ToConcurrentTestCase(
            this ConcurrentTestCaseFileRecord testCaseFileRecord,
            ISpec spec)
        {
            var segments = new List<TestCaseSegment>();
            var previousOperationCalls = new Dictionary<string, OperationCall>();

            foreach (var segmentFileRecord in testCaseFileRecord.Segments)
            {
                var operationCalls = new List<OperationCall>();

                foreach (var callFileRecord in segmentFileRecord.OperationCalls)
                {
                    var operationCall = callFileRecord.ToOperationCall(
                        spec,
                        previousOperationCalls);

                    operationCalls.Add(operationCall);
                    previousOperationCalls[operationCall.Name] = operationCall;
                }

                segments.Add(new TestCaseSegment(operationCalls));
            }

            return new ConcurrentTestCase()
            {
                Description = testCaseFileRecord.Description,
                Comments = testCaseFileRecord.Comments,
                Segments = segments
            };
        }

        public static ConcurrentTestCaseFileRecord ToConcurrentTestCaseFileRecord(
            this ConcurrentTestCase testCase,
            ISpec spec)
        {
            return new ConcurrentTestCaseFileRecord()
            {
                Description = testCase.Description,
                Comments = testCase.Comments,
                Segments = testCase.Segments
                    .Select(segment => new TestCaseSegmentFileRecord()
                    {
                        OperationCalls = segment.OperationCalls
                            .Select(call => call.ToOperationCallFileRecord(spec)).ToList()
                    }).ToList()
            };
        }

        public static OperationCall ToOperationCall(
            this OperationCallFileRecord operationCallFileRecord,
            ISpec spec,
            Dictionary<string, OperationCall> previousOperationCalls)
        {
            return new OperationCall(
                operationCallFileRecord.Name,
                operationCallFileRecord.Input.ToOperationInput(
                    spec,
                    previousOperationCalls));
        }

        public static OperationCallFileRecord ToOperationCallFileRecord(
            this OperationCall operationCall,
            ISpec spec)
        {
            return new OperationCallFileRecord()
            {
                Name = operationCall.Name,
                Input = operationCall.OperationInput.ToInputFileRecord(spec)
            };
        }

        public static OperationInput ToOperationInput(
            this InputFileRecord inputFileRecord,
            ISpec spec,
            Dictionary<string, OperationCall> previousOperationCalls)
        {
            var operation = spec.GetOperation(inputFileRecord.OperationName);

            object request = null;
            IList<OperationCall> derivedFromOperationCalls = null;

            if (inputFileRecord.DerivedFromOperationCalls != null)
            {
                derivedFromOperationCalls = new List<OperationCall>();

                foreach (var derivedFromCallName in inputFileRecord.DerivedFromOperationCalls)
                {

                    if (!previousOperationCalls.ContainsKey(derivedFromCallName))
                    {
                        var message = $"Operation call derives from \"{derivedFromCallName}\" but no such operation call found before it.";
                        throw new TestCasePersistenceException(message);
                    }

                    derivedFromOperationCalls.Add(previousOperationCalls[derivedFromCallName]);
                }
            }

            if (inputFileRecord.SerializedRequest != null)
            {
                request = JsonSerializer.Deserialize(
                    inputFileRecord.SerializedRequest,
                    operation.RequestType);
            }

            var operationInput = new OperationInput(
                inputFileRecord.Name,
                operation,
                request,
                derivedFromOperationCalls,
                inputFileRecord.DerivationVariant);

            operationInput.Polling = inputFileRecord.Polling;
            operationInput.SkipPolling = inputFileRecord.SkipPolling;

            return operationInput;
        }

        public static InputFileRecord ToInputFileRecord(
            this OperationInput input,
            ISpec spec)
        {
            return new InputFileRecord()
            {
                OperationName = spec.GetOperationName(input.Operation),
                Name = input.Name,
                SerializedRequest = input.Request == null ?
                    null :
                    JsonSerializer.Serialize(input.Request),
                DerivedFromOperationCalls = input.DerivedFromOperationCalls == null ?
                    null :
                    input.DerivedFromOperationCalls.Select(c => c.Name).ToList(),
                DerivationVariant = input.DerivationVariant,
                Polling = input.Polling,
                SkipPolling = input.SkipPolling
            };
        }
    }
}
