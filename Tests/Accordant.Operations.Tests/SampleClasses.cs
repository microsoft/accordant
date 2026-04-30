// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Operations.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Accordant;
    
    #region SimpleStatefulClass Spec and Operations

    public class SimpleStatefulClassSpec : Spec<CounterState>
    {
        public SimpleStatefulClassAddOperation AddOp { get; } = new();
        public SimpleStatefulClassCountOperation Count { get; } = new();
        public SimpleStatefulClassFaultWrappingAddOperation FaultWrappingAdd { get; } = new();
        public SimpleStatefulClassFaultWrappingCountOperation FaultWrappingCount { get; } = new();

        public SimpleStatefulClassSpec()
        {
            this["Add"] = AddOp;
            this["Count"] = Count;
            this["Fault Wrapping Add"] = FaultWrappingAdd;
            this["Fault Wrapping Count"] = FaultWrappingCount;
        }
    }

    public class SimpleStatefulClassAddOperation : Operation<int, int, CounterState>
    {
        public SimpleStatefulClassAddOperation() : base("Add") { }

        public override ExpectedOutcomes Apply(int request, CounterState state)
        {
            var expectedValue = request + state.Value;
            return Expect.That(r => r == expectedValue, $"should equal {expectedValue}")
                         .WithNextState(new CounterState(state.Value + request));
        }

        public override int Execute(TestingContext context, int request)
        {
            return context.Get<SimpleStatefulClass>().Add(request);
        }
    }

    public class SimpleStatefulClassCountOperation : Operation<Unit, int, CounterState>
    {
        public SimpleStatefulClassCountOperation() : base("Count") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            return Expect.That(r => r == state.Value, $"should equal {state.Value}")
                         .SameState();
        }

        public override int Execute(TestingContext context, Unit request)
        {
            return context.Get<SimpleStatefulClass>().GetCount();
        }
    }

    public class SimpleStatefulClassFaultWrappingAddOperation : Operation<int, int?, CounterState>
    {
        public SimpleStatefulClassFaultWrappingAddOperation() : base("Fault Wrapping Add") { }

        public override ExpectedOutcomes Apply(int request, CounterState state)
        {
            var expectedValue = request + state.Value;
            
            // Non-deterministic: either success or fault
            return Expect.OneOf(
                // Normal success outcome
                Expect.That(r => r == expectedValue, $"success: should equal {expectedValue}")
                      .WithNextState(new CounterState(state.Value + request)),
                // Exception/fault outcome - returns null, state unchanged
                Expect.That(r => r == null, "fault: should be null")
                      .SameState());
        }

        public override async Task<int?> ExecuteAsync(TestingContext context, int request)
        {
            try
            {
                var instance = context.Get<SimpleStatefulClass>();
                return await Task.FromResult(instance.Add(request));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public class SimpleStatefulClassFaultWrappingCountOperation : Operation<Unit, int?, CounterState>
    {
        public SimpleStatefulClassFaultWrappingCountOperation() : base("Fault Wrapping Count") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            // Non-deterministic: either success or fault
            return Expect.OneOf(
                // Normal success outcome
                Expect.That(r => r == state.Value, $"success: should equal {state.Value}")
                      .SameState(),
                // Exception/fault outcome - returns null, state unchanged
                Expect.That(r => r == null, "fault: should be null")
                      .SameState());
        }

        public override async Task<int?> ExecuteAsync(TestingContext context, Unit request)
        {
            try
            {
                var instance = context.Get<SimpleStatefulClass>();
                return await Task.FromResult(instance.GetCount());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    #endregion

    #region SimpleAsyncClass Spec and Operations

    public class SimpleAsyncClassSpec : Spec<CounterState>
    {
        public TriggerAsyncFirstStageOperation TriggerAsyncFirstStage { get; } = new();
        public TriggerSyncSecondStageOperation TriggerSyncSecondStage { get; } = new();
        public TriggerSyncThirdStageOperation TriggerSyncThirdStage { get; } = new();
        public GetStageOperation GetStage { get; } = new();

        public SimpleAsyncClassSpec()
        {
            this["TriggerAsyncFirstStage"] = TriggerAsyncFirstStage;
            this["TriggerSyncSecondStage"] = TriggerSyncSecondStage;
            this["TriggerSyncThirdStage"] = TriggerSyncThirdStage;
            this["GetStage"] = GetStage;
        }
    }

    #endregion

    //
    // Simple Stateful Class
    //

    public class SimpleStatefulClass
    {
        private bool simulateBuggyBehavior;
        private bool throwExceptionsOnAdd;
        private bool throwExceptionsOnGet;

        private int maxAddExceptions;
        private int maxGetExceptions;

        private int addExceptionCount = 0;
        private int getExceptionCount = 0;

        private int Count { get; set; } = 0;

        public SimpleStatefulClass(
            bool simulateBuggyBehavior = false,
            bool throwExceptionsOnAdd = false,
            bool throwExceptionsOnGet = false,
            int maxAddExceptions = -1,
            int maxGetExceptions = -1)
        {
            this.simulateBuggyBehavior = simulateBuggyBehavior;
            this.throwExceptionsOnAdd = throwExceptionsOnAdd;
            this.throwExceptionsOnGet = throwExceptionsOnGet;
            this.maxAddExceptions = maxAddExceptions;
            this.maxGetExceptions = maxGetExceptions;
        }

        public int Add(int value)
        {
            if (throwExceptionsOnAdd)
            {
                if (maxAddExceptions == -1 ||
                    addExceptionCount < maxAddExceptions)
                {
                    addExceptionCount++;

                    throw new System.Exception();
                }
            }

            lock (this)
            {
                if (simulateBuggyBehavior)
                {
                    Count += (value - 1);
                }
                else
                {
                    Count += value;
                }

                return Count;
            }
        }

        public int GetCount()
        {
            if (throwExceptionsOnGet)
            {
                if (maxGetExceptions == -1 ||
                    getExceptionCount < maxGetExceptions)
                {
                    getExceptionCount++;

                    throw new System.Exception();
                }
            }

            lock (this)
            {
                return Count;
            }
        }
    }

    //
    // Simple Async Class
    //

    public class SimpleAsyncClass
    {
        private int stage = 0;

        public void TriggerAsyncFirstStage()
        {
            if (stage != 0)
            {
                return;
            }

            stage = 1;

            _ = Task.Run(async () =>
            {
                await Task.Delay(10);

                stage = 2;
            });
        }

        public bool TriggerSyncSecondStage()
        {
            if (stage < 2)
            {
                return false;
            }

            stage = 3;

            return true;
        }

        public bool TriggerSyncThirdStage()
        {
            if (stage < 3)
            {
                return false;
            }

            stage = 4;

            return true;
        }

        public int GetStage()
        {
            return stage;
        }
    }

    public class TriggerAsyncFirstStageOperation : Operation<Unit, Unit, CounterState>
    {
        public TriggerAsyncFirstStageOperation() : base("TriggerAsyncFirstStage") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            if (state.Value != 0)
            {
                return Expect.Unit("already triggered")
                             .SameState();
            }

            return Expect.Unit("trigger async work")
                         .WithNextState(new CounterState(1))
                         .Triggers(new SimpleAsyncClassStepFunction());
        }

        public override Unit Execute(TestingContext context, Unit request)
        {
            context.Get<SimpleAsyncClass>().TriggerAsyncFirstStage();
            return Unit.Value;
        }
    }

    public class TriggerSyncSecondStageOperation : Operation<Unit, bool, CounterState>
    {
        public TriggerSyncSecondStageOperation() : base("TriggerSyncSecondStage") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            if (state.Value != 2)
            {
                return Expect.That(b => b == false, "not ready for second stage")
                             .SameState();
            }

            return Expect.That(b => b == true, "second stage triggered")
                         .WithNextState(new CounterState(3));
        }

        public override bool Execute(TestingContext context, Unit request)
        {
            return context.Get<SimpleAsyncClass>().TriggerSyncSecondStage();
        }
    }

    public class TriggerSyncThirdStageOperation : Operation<Unit, bool, CounterState>
    {
        public TriggerSyncThirdStageOperation() : base("TriggerSyncThirdStage") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            if (state.Value != 3)
            {
                return Expect.That(b => b == false, "not ready for third stage")
                             .SameState();
            }

            return Expect.That(b => b == true, "third stage triggered")
                         .WithNextState(new CounterState(4));
        }

        public override bool Execute(TestingContext context, Unit request)
        {
            return context.Get<SimpleAsyncClass>().TriggerSyncThirdStage();
        }
    }

    public class GetStageOperation : Operation<Unit, int, CounterState>
    {
        public GetStageOperation() : base("GetStage") { }

        public override ExpectedOutcomes Apply(Unit request, CounterState state)
        {
            return Expect.That(r => r == state.Value, $"should equal current stage {state.Value}")
                         .SameState();
        }

        public override int Execute(TestingContext context, Unit request)
        {
            return context.Get<SimpleAsyncClass>().GetStage();
        }
    }

    public class SimpleAsyncClassStepFunction : TerminatingStepFunction
    {
        /// <summary>
        /// Terminal when state value is 2 (async work completed).
        /// </summary>
        public override Func<IState, bool> IsTerminalState => state =>
        {
            var counterState = (CounterState)state;
            return counterState.Value == 2;
        };

        /// <summary>
        /// Transition from value 1 to value 2.
        /// Only called when IsTerminalState is false.
        /// </summary>
        protected override IList<StepResult> GetStepResults(IState state)
        {
            var nextState = (CounterState)state.Clone();
            if (nextState.Value == 1)
            {
                nextState.Value = 2;
            }
            return new[] { new StepResult { State = nextState } };
        }
    }
}
