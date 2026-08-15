namespace WalRefinement;

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Accordant;

/// <summary>
/// One action of a model, declared inline: a stable id, a guard, and one
/// atomic mutation of the next state.
///
/// <para>Applying an action re-emits it, so the set of actions — and with it
/// the identity of a graph node — never changes, the way the set of actions of
/// a TLA+ specification is constant. An action whose mutation left the state
/// untouched would be invisible to changing-edge fairness; none of the actions
/// in this sample does that.</para>
/// </summary>
/// <typeparam name="TState">The state the action reads and writes.</typeparam>
public abstract class Step<TState> : BaseStepFunction
    where TState : State
{
    private readonly Func<TState, bool> when;
    private readonly Action<TState> then;

    protected Step(
        string id,
        string subject,
        Func<TState, bool> when,
        Action<TState> then)
    {
        StepFunctionId = id;
        Subject = subject;
        this.when = when ?? throw new ArgumentNullException(nameof(when));
        this.then = then ?? throw new ArgumentNullException(nameof(then));
    }

    /// <summary>The stable id, used in traces and diagnostics.</summary>
    public override string StepFunctionId { get; }

    /// <summary>
    /// What this instance of the action ranges over — a write-set name, a
    /// key — or <c>null</c> for an action with a single instance.
    /// </summary>
    public string Subject { get; }

    protected override IList<StepResult> ApplyInternal(IState state)
    {
        var source = (TState)state;
        if (!when(source))
        {
            return null;
        }

        var next = (TState)source.Clone();
        then(next);
        return new[]
        {
            new StepResult
            {
                State = next,
                StepFunctions = new IStepFunction[] { this }
            }
        };
    }

    public override string ToString() => StepFunctionId;
}

/// <summary>Naming for inline-declared actions.</summary>
public static class Step
{
    /// <summary>
    /// The id of an action: <c>AppendRedo</c> is <c>append-redo</c>,
    /// <c>InstallData</c> over <c>k0</c> is <c>install-data-k0</c>, and
    /// <c>Commit</c> under the prefix <c>spec</c> is <c>spec-commit</c>.
    /// </summary>
    public static string Id(Enum action, string subject = null, string prefix = null)
    {
        var id = new StringBuilder();
        if (prefix != null)
        {
            id.Append(prefix).Append('-');
        }

        var name = action.ToString();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                id.Append('-');
            }

            id.Append(char.ToLowerInvariant(name[i]));
        }

        return subject == null ? id.ToString() : id.Append('-').Append(subject).ToString();
    }
}
