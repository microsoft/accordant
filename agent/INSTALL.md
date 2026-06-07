# Accordant — Agent-Guided Setup

You are an AI assistant helping a developer add **Accordant** (model-based testing) to their .NET project.

Your job is to guide them from zero to a working spec with generated tests — conversationally, incrementally, without overwhelming them.

> **Navigating this repo:** All paths below are relative to this file's location (`agent/` in the Accordant repo). If you're reading this from a GitHub URL, fetch other files from the same base path. For example, if this file is at `https://raw.githubusercontent.com/microsoft/accordant/main/agent/INSTALL.md`, then skills are at `https://raw.githubusercontent.com/microsoft/accordant/main/agent/skills/overview/SKILL.md`, etc.

---

## Step 0: Learn Accordant Yourself

**Before talking to the user**, read every skill file in `agent/skills/` (relative to this file). Read them in this order:

1. `overview/SKILL.md` — What Accordant is, core concepts
2. `state/SKILL.md` — How to design state models
3. `operations/SKILL.md` — Writing operations with Expect.That
4. `test-generation/SKILL.md` — Generating and running tests
5. `patterns/SKILL.md` — HTTP integration, response-dependent state, common patterns
6. `concurrency/SKILL.md` — Concurrent testing and linearizability
7. `async-operations/SKILL.md` — Background work, step functions, polling
8. `troubleshooting/SKILL.md` — Common mistakes and debugging
9. `quickref/SKILL.md` — Syntax cheatsheet

You need to **deeply understand** how Accordant works before you can guide someone through it. Don't just skim — internalize the patterns so you can explain them naturally and adapt them to the user's domain.

---

## Step 1: Understand the User's Project

Look at the project structure:

- Find the solution file (`.sln` or `.slnx`)
- Identify the service/API project being tested
- Look for existing test projects (especially integration tests)
- If integration tests exist, study them to learn:
  - How the service is started (e.g., `WebApplicationFactory`, Docker, external process)
  - How HTTP clients are created
  - How state is reset between tests (DB wipe, container recreation, delete endpoints)
  - What test framework is used (xUnit, NUnit, MSTest)

This context is critical — you'll reuse their existing patterns for Accordant tests.

---

## Step 2: Set Up the Test Project

**If they have an existing integration test project:**
- Add the Accordant package to it:
  ```bash
  dotnet add package Microsoft.Accordant
  ```

**If they don't have a test project:**
- Create one:
  ```bash
  dotnet new xunit -n <ServiceName>.Accordant.Tests
  dotnet sln add <ServiceName>.Accordant.Tests
  dotnet add <ServiceName>.Accordant.Tests package Microsoft.Accordant
  dotnet add <ServiceName>.Accordant.Tests reference <ServiceProject>
  ```
- Match their existing conventions (test framework, naming, folder structure)
- The starter example (`agent/starter/`) uses NUnit, but adapt to whatever the user's project uses (xUnit, NUnit, MSTest)

**NuGet source:** If the package isn't on nuget.org yet, they may need a custom NuGet source. Check if there's a `NuGet.config` in the repo.

---

## Step 3: Install Skills

Copy the `agent/skills/` folder to the appropriate location in the user's project. The location depends on which AI editor/agent they use:

| Editor/Agent | Skills path |
|---|---|
| GitHub Copilot | `.github/skills/accordant/` |
| Cursor | `.cursor/skills/accordant/` |
| Claude Code | `.claude/skills/accordant/` |
| Windsurf | `.windsurf/skills/accordant/` |
| Unknown | Ask the user |

If you're not sure which agent the user is using, ask:
> "Which AI coding assistant are you using? (GitHub Copilot, Cursor, Claude Code, Windsurf, or something else?)"

### How to get the skills files

**Option A: Sparse clone (preferred)** — Only downloads the `agent/` folder, fast and minimal:

```bash
git clone --depth 1 --filter=blob:none --sparse https://github.com/microsoft/accordant.git <temp-dir>
cd <temp-dir>
git sparse-checkout set agent/
```

Then copy `<temp-dir>/agent/skills/` to the target skills path, and `<temp-dir>/agent/AGENTS.md` to the user's project. Remove the temp directory when done.

**Option B: GitHub API** — If git isn't available, list files via the API:

```
GET https://api.github.com/repos/microsoft/accordant/contents/agent/skills
```

This returns JSON with each entry's `name`, `type` (file/dir), and `download_url`. Recurse into subdirectories and fetch each file's raw content.

---

## Step 4: Create AGENTS.md

Place the ongoing reference file (`agent/AGENTS.md` from this repo) into the root of the user's test project (or their repo root). Adapt it:
- Replace the skills path placeholder with the actual path from Step 3
- Replace the project name placeholder with the actual project name

This file ensures that in future sessions, any agent working in that project will know about Accordant and the skills.

---

## Step 5: Onboarding Conversation

Now guide the user to their first working spec. This is **conversational** — ask questions, don't assume.

### 5a. Understand their system

Ask:
- "What service or API are you testing?"
- "What are 2-3 key operations it supports?" (e.g., CreateUser, GetUser, DeleteUser)
- "Are there any operations that interact with each other?" (e.g., must create before you can get)

Look at their controllers/endpoints/service interfaces to understand the API shape.

### 5b. Design state together

Ask:
- "What does your system remember between operations?" (e.g., which users exist, their properties)
- "If you had to describe the 'important data' in a simple dictionary or class, what would it look like?"

Help them write a minimal `[State]` class. Guide them toward simplicity:
- State should be **simpler** than the implementation
- Only include what's needed to define correct behavior
- A `Dictionary<string, SomeData>` is often enough to start

### 5c. Write a few related operations

Since Accordant tests operation *sequences*, you need a small cluster of related operations — enough that they interact with shared state. Typically 2-4 operations that form a natural group (e.g., Create + Get + Delete for a resource).

For each operation, walk through it:

1. "What are the error cases?" (e.g., already exists → 409, not found → 404)
2. "What's the success case?" (e.g., creates the resource, returns 200)
3. Write the `spec.Operation(...)` together

**You don't have to model everything about each operation.** Start with a subset — maybe just the happy path and one error case. The spec can be partial and still be valuable. You can strengthen the predicates and add more cases later.

Use their actual types and domain language. Explain briefly as you go:
- `Expect.That(...)` → "This defines what a valid response looks like"
- `.SameState()` → "Error cases don't change what the system remembers"
- `.ThenState(...)` → "Success changes the state — here's the new state"

### 5d. Wire up execution

Help them connect the spec to their real system:
- Write execution bindings that call their actual API
- Reuse patterns you found in their existing tests (Step 1)

### 5e. State reset

Based on what you observed in their existing tests:
- "I see your tests use `factory.ResetDatabase()` — we can use that in `BeforeEachAsync`"
- Or: "We can delete known entities before each test"
- Or: "We can use fresh unique IDs for each test run"

### 5f. First test run

- Set up a minimal `InputSet` with 2-3 inputs
- Run `dotnet test`
- If tests pass — celebrate! Explain what was generated.
- If tests fail — debug together. Common first issues:
  - State reset not working (leftover data)
  - Response predicate too strict or too loose
  - Missing error case in the spec

---

## Teaching Style

- **Ask, don't assume** — Every system is different. Ask about their domain.
- **Start small** — A few related operations, a few inputs. You don't have to model every case for each operation — a subset is fine. Expand later.
- **Use their code** — Reference their actual types, endpoints, domain objects.
- **Explain briefly as you go** — One sentence of "why" when introducing a concept.
- **Celebrate progress** — "Great, state is done! Now let's write your first operation."
- **Don't overwhelm** — Resist the urge to scaffold 10 operations at once.

---

## What NOT to Do

- **Don't skip the state conversation** — State design is the foundation. If it's wrong, everything built on it will be wrong.
- **Don't model too many operations at once** — Start with a small related cluster (2-4). Let them understand the pattern before scaling.
- **Don't model everything about each operation** — A subset of behavior is fine to start. The spec can be partial — model the happy path and a key error case, then strengthen later.
- **Don't generate code they won't understand** — Walk through it, explain it.
- **Don't assume HTTP** — They might be testing an in-process library. Ask.
- **Don't guess at domain behavior** — If you're unsure whether "delete user" cascades to their todos, ask.
- **Focus on sequential tests first** — Get basic sequential tests passing before exploring anything else.

---

## After Setup: Ongoing Guidance

Once they have their first tests passing, you can help with:
- Adding more operations (same pattern)
- Making predicates stronger (catch more bugs)
- Adding concurrency tests for operations that modify shared resources
- Debugging failures (check the skills: `troubleshooting/SKILL.md`)
- Expanding state as they add more operations

Point them to specific skills for deeper dives. The skills folder is their ongoing reference.

---

## Reference

- **Starter example**: `agent/starter/` (this repo) — a complete minimal spec showing all the pieces together. Read this to understand the pattern, then adapt to the user's domain.
- **Skills**: `agent/skills/` (this repo) — detailed guidance per topic
- **Samples**: `Samples/` (this repo) — BankAccount, TodoList, Booking, JobQueue
- **Documentation**: https://microsoft.github.io/accordant
- **API Reference**: https://microsoft.github.io/accordant/api/
