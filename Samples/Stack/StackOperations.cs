// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Stack
{
    using Microsoft.Accordant;
    /// <summary>
    /// Push operation - adds an element to the top of the stack.
    /// </summary>
    public class PushOperation<TElement> :
        Operation<TElement, Unit, StackState<TElement>>
    {
        public PushOperation() : base("Push") { }

        public override ExpectedOutcomes Apply(
            TElement request,
            StackState<TElement> state)
        {
            return Expect.Unit()
                         .ThenState(nextState => nextState.Items.Add(request));
        }

        public override Unit Execute(TestingContext context, TElement request)
        {
            context.Get<Stack<TElement>>().Push(request);
            return Unit.Value;
        }
    }

    /// <summary>
    /// Peek operation - returns the top element without removing it.
    /// </summary>
    public class PeekOperation<TElement> :
        Operation<Unit, TElement, StackState<TElement>>
    {
        public PeekOperation() : base("Peek") { }

        public override ExpectedOutcomes Apply(
            Unit request,
            StackState<TElement> state)
        {
            if (state.Items.Count > 0)
            {
                var topElement = state.Items[state.Items.Count - 1];
                return Expect.That(
                           r => r.Equals(topElement),
                           $"should return top element {topElement}")
                       .SameState();
            }
            else
            {
                return Expect.Throws<EmptyStackException>()
                       .SameState();
            }
        }

        public override TElement Execute(TestingContext context, Unit request)
        {
            return context.Get<Stack<TElement>>().Peek();
        }
    }

    /// <summary>
    /// Pop operation - removes and returns the top element.
    /// </summary>
    public class PopOperation<TElement> :
        Operation<Unit, TElement, StackState<TElement>>
    {
        public PopOperation() : base("Pop") { }

        public override ExpectedOutcomes Apply(
            Unit request,
            StackState<TElement> state)
        {
            if (state.Items.Count > 0)
            {
                var topElement = state.Items[state.Items.Count - 1];

                return Expect.That(
                           r => r.Equals(topElement),
                           $"should return top element {topElement}")
                       .ThenState(nextState => nextState.Items.RemoveAt(nextState.Items.Count - 1));
            }
            else
            {
                return Expect.Throws<EmptyStackException>()
                       .SameState();
            }
        }

        public override TElement Execute(TestingContext context, Unit request)
        {
            return context.Get<Stack<TElement>>().Pop();
        }
    }

    /// <summary>
    /// Count operation - returns the number of elements in the stack.
    /// </summary>
    public class CountOperation<TElement> :
        Operation<Unit, int, StackState<TElement>>
    {
        public CountOperation() : base("Count") { }

        public override ExpectedOutcomes Apply(
            Unit request,
            StackState<TElement> state)
        {
            return Expect.That(r => r == state.Items.Count, $"should equal {state.Items.Count}")
                         .SameState();
        }

        public override int Execute(TestingContext context, Unit request)
        {
            return context.Get<Stack<TElement>>().Count();
        }
    }

    /// <summary>
    /// IsEmpty operation - returns whether the stack is empty.
    /// </summary>
    public class IsEmptyOperation<TElement> :
        Operation<Unit, bool, StackState<TElement>>
    {
        public IsEmptyOperation() : base("IsEmpty") { }

        public override ExpectedOutcomes Apply(
            Unit request,
            StackState<TElement> state)
        {
            var expected = state.Items.Count == 0;
            return Expect.That(r => r == expected, $"should be {expected}")
                         .SameState();
        }

        public override bool Execute(TestingContext context, Unit request)
        {
            return context.Get<Stack<TElement>>().IsEmpty();
        }
    }
}
