namespace WorkQueueRefinement;

using System.Collections.Generic;
using Microsoft.Accordant;

/// <summary>The lifecycle of one queue entry as the implementation sees it.</summary>
public enum TaskPhase
{
    /// <summary>Waiting in the queue for a worker to take a lease.</summary>
    Ready,

    /// <summary>A worker holds the lease and is running an attempt.</summary>
    Leased,

    /// <summary>An attempt succeeded.</summary>
    Completed,

    /// <summary>The retry budget was exhausted by failing attempts.</summary>
    Dropped,

    /// <summary>A cancellation request was honored.</summary>
    Cancelled,

    /// <summary>The queue removed a repeatedly failing entry.</summary>
    Purged
}

/// <summary>The number of workers, tasks, and attempts in one model instance.</summary>
public sealed class WorkQueueConfig
{
    /// <summary>The default two workers competing for two tasks with one retry.</summary>
    public static WorkQueueConfig Default { get; } = new WorkQueueConfig(2, 2, 2);

    public WorkQueueConfig(int workers, int tasks, int maxAttempts)
    {
        Workers = workers;
        Tasks = tasks;
        MaxAttempts = maxAttempts;
    }

    public int Workers { get; }
    public int Tasks { get; }

    /// <summary>The number of attempts a task gets before it is dropped.</summary>
    public int MaxAttempts { get; }
}

/// <summary>
/// The implementation: a leased work queue. Workers compete for tasks, an
/// attempt may fail and be retried, a lease may expire without consuming an
/// attempt, a client may request cancellation, and the queue may purge an
/// entry that keeps failing.
/// </summary>
[State]
public partial class QueueState
{
    /// <summary>The phase of each task.</summary>
    public TaskPhase[] Phases { get; set; }

    /// <summary>The number of attempts each task has already spent.</summary>
    public int[] Attempts { get; set; }

    /// <summary>Whether a cancellation request has arrived for each task.</summary>
    public bool[] CancelRequested { get; set; }

    /// <summary>
    /// The task each worker currently holds a lease on, or
    /// <see cref="WorkQueue.Idle"/>.
    /// </summary>
    public int[] WorkerTask { get; set; }
}

/// <summary>Builds the implementation model.</summary>
public static class WorkQueue
{
    /// <summary>A worker that holds no lease.</summary>
    public const int Idle = -1;

    /// <summary>Creates the initial state: every task ready, every worker idle.</summary>
    public static QueueState InitialState(WorkQueueConfig config)
    {
        var workerTask = new int[config.Workers];
        for (var worker = 0; worker < config.Workers; worker++)
        {
            workerTask[worker] = Idle;
        }

        return new QueueState
        {
            Phases = new TaskPhase[config.Tasks],
            Attempts = new int[config.Tasks],
            CancelRequested = new bool[config.Tasks],
            WorkerTask = workerTask
        };
    }

    /// <summary>Creates every step function of the implementation model.</summary>
    public static IList<IStepFunction> Steps(WorkQueueConfig config)
    {
        var steps = new List<IStepFunction>();
        for (var worker = 0; worker < config.Workers; worker++)
        {
            for (var task = 0; task < config.Tasks; task++)
            {
                steps.Add(new LeaseStep(worker, task, config.MaxAttempts));
                steps.Add(new ExpireLeaseStep(worker, task));
                steps.Add(new CompleteStep(worker, task));
                steps.Add(new FailStep(worker, task, config.MaxAttempts));
                steps.Add(new ObserveCancelStep(worker, task));
            }
        }

        for (var task = 0; task < config.Tasks; task++)
        {
            steps.Add(new RequestCancelStep(task));
            steps.Add(new CancelReadyStep(task));
            steps.Add(new PurgeStep(task));
        }

        return steps;
    }

    /// <summary>Explores the implementation graph on demand.</summary>
    public static StateGraphNode Explore(WorkQueueConfig config)
        => StateGraph.ExploreStateGraph(
            Steps(config),
            InitialState(config),
            lazy: true);
}

/// <summary>
/// Shared scaffolding: guard the source state, clone it, mutate the clone,
/// and re-emit this step so the step-function set — and therefore the graph
/// node identity — stays stable.
/// </summary>
public abstract class WorkQueueStep : BaseStepFunction
{
    protected abstract bool IsEnabled(QueueState queue);

    protected abstract void Advance(QueueState next);

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var queue = (QueueState)state;
        if (!IsEnabled(queue))
        {
            return null;
        }

        var next = (QueueState)queue.Clone();
        Advance(next);
        return new[]
        {
            new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }
}

/// <summary>A worker takes the lease on a ready task.</summary>
public sealed class LeaseStep : WorkQueueStep
{
    private readonly int maxAttempts;

    public LeaseStep(int worker, int task, int maxAttempts)
    {
        Worker = worker;
        Task = task;
        this.maxAttempts = maxAttempts;
    }

    public int Worker { get; }
    public int Task { get; }

    public override string StepFunctionId => $"lease-w{Worker}-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.WorkerTask[Worker] == WorkQueue.Idle &&
            queue.Phases[Task] == TaskPhase.Ready &&
            !queue.CancelRequested[Task] &&
            queue.Attempts[Task] < maxAttempts;

    protected override void Advance(QueueState next)
    {
        next.Phases[Task] = TaskPhase.Leased;
        next.WorkerTask[Worker] = Task;
    }
}

/// <summary>
/// The lease expires — a slow or crashed worker loses the task without
/// spending an attempt. This is the model's source of infinite behavior.
/// </summary>
public sealed class ExpireLeaseStep : WorkQueueStep
{
    public ExpireLeaseStep(int worker, int task)
    {
        Worker = worker;
        Task = task;
    }

    public int Worker { get; }
    public int Task { get; }

    public override string StepFunctionId => $"expire-w{Worker}-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.WorkerTask[Worker] == Task;

    protected override void Advance(QueueState next)
    {
        next.Phases[Task] = TaskPhase.Ready;
        next.WorkerTask[Worker] = WorkQueue.Idle;
    }
}

/// <summary>The attempt succeeds.</summary>
public sealed class CompleteStep : WorkQueueStep
{
    public CompleteStep(int worker, int task)
    {
        Worker = worker;
        Task = task;
    }

    public int Worker { get; }
    public int Task { get; }

    public override string StepFunctionId => $"complete-w{Worker}-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.WorkerTask[Worker] == Task && !queue.CancelRequested[Task];

    protected override void Advance(QueueState next)
    {
        next.Phases[Task] = TaskPhase.Completed;
        next.Attempts[Task]++;
        next.WorkerTask[Worker] = WorkQueue.Idle;
    }
}

/// <summary>
/// The attempt fails. The task returns to the queue while the retry budget
/// lasts, and is dropped when the budget runs out.
/// </summary>
public sealed class FailStep : WorkQueueStep
{
    private readonly int maxAttempts;

    public FailStep(int worker, int task, int maxAttempts)
    {
        Worker = worker;
        Task = task;
        this.maxAttempts = maxAttempts;
    }

    public int Worker { get; }
    public int Task { get; }

    public override string StepFunctionId => $"fail-w{Worker}-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.WorkerTask[Worker] == Task && !queue.CancelRequested[Task];

    protected override void Advance(QueueState next)
    {
        next.Attempts[Task]++;
        next.Phases[Task] = next.Attempts[Task] >= maxAttempts
            ? TaskPhase.Dropped
            : TaskPhase.Ready;
        next.WorkerTask[Worker] = WorkQueue.Idle;
    }
}

/// <summary>The worker notices the cancellation request and abandons the task.</summary>
public sealed class ObserveCancelStep : WorkQueueStep
{
    public ObserveCancelStep(int worker, int task)
    {
        Worker = worker;
        Task = task;
    }

    public int Worker { get; }
    public int Task { get; }

    public override string StepFunctionId => $"observe-cancel-w{Worker}-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.WorkerTask[Worker] == Task && queue.CancelRequested[Task];

    protected override void Advance(QueueState next)
    {
        next.Phases[Task] = TaskPhase.Cancelled;
        next.WorkerTask[Worker] = WorkQueue.Idle;
    }
}

/// <summary>A client asks for the task to be cancelled.</summary>
public sealed class RequestCancelStep : WorkQueueStep
{
    public RequestCancelStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"request-cancel-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => !queue.CancelRequested[Task] &&
            (queue.Phases[Task] == TaskPhase.Ready ||
                queue.Phases[Task] == TaskPhase.Leased);

    protected override void Advance(QueueState next)
        => next.CancelRequested[Task] = true;
}

/// <summary>The queue cancels an unleased task that a client asked to cancel.</summary>
public sealed class CancelReadyStep : WorkQueueStep
{
    public CancelReadyStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"cancel-ready-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.Phases[Task] == TaskPhase.Ready && queue.CancelRequested[Task];

    protected override void Advance(QueueState next)
        => next.Phases[Task] = TaskPhase.Cancelled;
}

/// <summary>
/// The queue removes an entry that already burned an attempt and is waiting
/// to be retried. The client never learns an outcome for it.
/// </summary>
public sealed class PurgeStep : WorkQueueStep
{
    public PurgeStep(int task)
    {
        Task = task;
    }

    public int Task { get; }

    public override string StepFunctionId => $"purge-t{Task}";

    protected override bool IsEnabled(QueueState queue)
        => queue.Phases[Task] == TaskPhase.Ready &&
            queue.Attempts[Task] >= 1 &&
            !queue.CancelRequested[Task];

    protected override void Advance(QueueState next)
        => next.Phases[Task] = TaskPhase.Purged;
}
