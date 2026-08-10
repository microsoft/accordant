using Microsoft.Accordant;
using Microsoft.Accordant.ModelChecking.Experimental.Coroutines;

namespace CoroutineModelChecking;

internal static class Program
{
    private static void Main()
    {
        var graph = CoroutineModel.Explore("counter-workflow", new CounterState(), Workflow);
        Console.WriteLine(graph.GenerateDotFileContent());
    }

    private static async ModelTask Workflow(ModelContext<CounterState> context)
    {
        var initial = await context.Read("initial", state => state.Count);
        var delta = await context.Choose("delta", new[] { 1, 2 });
        await context.Step("apply-delta", state => state.Count = initial + delta);
        await context.Step("mark-complete", state => state.Completed = true);
    }
}

sealed class CounterState : State
{
    public int Count { get; set; }
    public bool Completed { get; set; }

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
        => clonedMap[this] = new CounterState { Count = Count, Completed = Completed };

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
        => $"Count={Count},Completed={Completed}";

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }
}
