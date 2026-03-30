// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant.Examples;

/// <summary>
/// Simple Stack implementation for testing.
/// This is the "system under test" that the spec validates.
/// </summary>
public class Stack<T>
{
    private readonly List<T> _items = new();

    public void Push(T item)
    {
        _items.Add(item);
    }

    public T Pop()
    {
        if (_items.Count == 0)
            throw new EmptyStackException();
        
        var item = _items[_items.Count - 1];
        _items.RemoveAt(_items.Count - 1);
        return item;
    }

    public T Peek()
    {
        if (_items.Count == 0)
            throw new EmptyStackException();
        
        return _items[_items.Count - 1];
    }

    public int Count() => _items.Count;

    public bool IsEmpty() => _items.Count == 0;
}
