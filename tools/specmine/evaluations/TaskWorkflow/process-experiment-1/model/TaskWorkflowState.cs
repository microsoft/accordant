using System.Text;
using Microsoft.Accordant;

namespace TaskWorkflow.ProcessExperiment1.Model;

internal sealed class TaskWorkflowState : State
{
    private readonly SortedDictionary<string, TaskSnapshot> tasks;

    public TaskWorkflowState()
        : this(new SortedDictionary<string, TaskSnapshot>(StringComparer.Ordinal))
    {
    }

    private TaskWorkflowState(SortedDictionary<string, TaskSnapshot> tasks)
    {
        this.tasks = tasks;
    }

    public bool TryGetTask(string id, out TaskSnapshot task) => tasks.TryGetValue(id, out task!);

    public void AddOrUpdate(TaskSnapshot task)
    {
        if (IsFrozen)
        {
            throw new StateFrozenException("TaskWorkflowState is frozen and cannot be mutated.");
        }

        tasks[task.Id] = task;
    }

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
    {
        clonedMap[this] = new TaskWorkflowState(new SortedDictionary<string, TaskSnapshot>(tasks, StringComparer.Ordinal));
    }

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
    {
        if (tasks.Count == 0)
        {
            return "tasks=[]";
        }

        var builder = new StringBuilder("tasks=[");
        var first = true;

        foreach (var (id, task) in tasks)
        {
            if (!first)
            {
                builder.Append(';');
            }

            first = false;
            builder.Append(id);
            builder.Append('|');
            builder.Append(task.Title.Replace("|", "||", StringComparison.Ordinal));
            builder.Append('|');
            builder.Append(task.Status);
        }

        builder.Append(']');
        return builder.ToString();
    }
}
