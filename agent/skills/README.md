# Accordant AI Skills

AI coding assistant skills for the Accordant model-based testing framework.

## Available Skills

| Skill | When to Use |
|-------|-------------|
| [overview](overview/SKILL.md) | Starting with Accordant, understanding what it is and when to use it |
| [state](state/SKILL.md) | Designing state models, the `[State]` attribute |
| [operations](operations/SKILL.md) | Writing operations with Apply/Execute, `Expect.That` |
| [test-generation](test-generation/SKILL.md) | Generating tests, state graphs, running tests |
| [concurrency](concurrency/SKILL.md) | Testing race conditions, linearizability |
| [async-operations](async-operations/SKILL.md) | Background work, step functions, polling |
| [patterns](patterns/SKILL.md) | Response-dependent state, HTTP integration, error handling |
| [troubleshooting](troubleshooting/SKILL.md) | Debugging failures, common mistakes |
| [quickref](quickref/SKILL.md) | Quick syntax lookup, cheatsheet |

## Suggested Learning Path

**New to Accordant?**
1. `overview` → Understand what Accordant is
2. `state` → Learn to model state
3. `operations` → Write your first operations
4. `test-generation` → Generate and run tests

**Going deeper?**
5. `concurrency` → Find race conditions
6. `async-operations` → Handle background work
7. `patterns` → Common patterns and integration

**Having issues?**
- `troubleshooting` → Debug problems
- `quickref` → Quick syntax lookup

## For AI Assistants

When helping users with Accordant:
- Use `overview` for "what is Accordant" or "should I use this" questions
- Use `state` when user is defining state classes or asks about `[State]`
- Use `operations` when user is writing Apply methods or using `Expect`
- Use `test-generation` when user wants to generate or run tests
- Use `concurrency` for race conditions or concurrent testing
- Use `async-operations` for jobs, queues, polling, or `.Triggers()`
- Use `patterns` for HTTP APIs, server-generated IDs, or common patterns
- Use `troubleshooting` when tests fail or something isn't working
- Use `quickref` for quick code snippets or syntax questions
