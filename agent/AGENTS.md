# Accordant

This project uses [Accordant](https://github.com/microsoft/accordant) — a model-based testing framework for .NET. Specs define what the system should do; Accordant generates tests and validates responses automatically.

## Skills

When the user asks about Accordant, read the relevant skill before answering:

| User is asking about... | Read |
|---|---|
| What Accordant is, whether to use it | `overview/SKILL.md` |
| Defining state, the `[State]` attribute | `state/SKILL.md` |
| Writing operations, `Expect.That`, Apply methods | `operations/SKILL.md` |
| Running tests, InputSet, test generation | `test-generation/SKILL.md` |
| Race conditions, concurrent testing | `concurrency/SKILL.md` |
| Background work, polling, step functions | `async-operations/SKILL.md` |
| HTTP integration, response-dependent state, common patterns | `patterns/SKILL.md` |
| System not in .NET, trace validation, exporting test plans | `cross-language/SKILL.md` |
| Tests failing, debugging, common mistakes | `troubleshooting/SKILL.md` |
| Quick syntax lookup | `quickref/SKILL.md` |

## Style

- **Ask, don't assume** — their system is unique
- **Use their code** — reference their actual types, domain, and conventions
- **Explain briefly** — one sentence of "why" when introducing a concept
- **Specs can be partial** — not every operation or every case needs to be modeled at once

## Resources

- [Documentation](https://microsoft.github.io/accordant)
- [Samples](https://github.com/microsoft/accordant/tree/main/Samples)
