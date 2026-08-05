# CLAUDE.md

## Core principles

Follow DDD, SOLID, and idiomatic modern .NET. The rules that actually steer day-to-day
code here:
- Business logic belongs in the domain layer, not in application services (rich domain models).
- Depend on abstractions, not concretions; keep types single-responsibility and open for extension.
- Use async/await for I/O-bound work; use the built-in DI container for loose coupling.
- Prefer modern C# (records, pattern matching, LINQ) and a consistent exception-handling strategy.

## Code style
- Use Gang of Four design patterns.
- Comment all classes and public methods with clear and concise comments.
- All async methods end with Async.
- One file, one primary type: every public class, struct, enum, or interface gets its own .cs file named to match the declared type.
- Namespaces mirror folder/project names (e.g. `Backtester.Core`, `Backtester.Engine`).
- A project's folder, `.csproj` basename, namespace and package id all derive from one name. For a
  project named `Stops`: folder `Stops/`, project file `Stops/Stops.csproj`, namespace
  `Backtester.Stops`, `PackageId` `backtester.net.stops`. The rule: folder == `.csproj` basename;
  namespace == `Backtester.<name>`; `PackageId` == the namespace lowercased with `Backtester`
  replaced by `backtester.net`. The `Backtester` prefix lives in the namespace and the package id
  only — never in a folder or file name. A multi-segment name keeps every segment throughout, so
  `Data.Alpaca/` is namespace `Backtester.Data.Alpaca` and package `backtester.net.data.alpaca`.
- Don't wrap a project in a solution folder that just repeats its own name; solution folders are for
  grouping several projects (as `samples` does).
- Use block-scoped namespaces (`namespace X { ... }`), not file-scoped.
- Tests follow the same rule: one test class per file; test file names mirror the production type (`PortfolioTests.cs` tests `Portfolio`).
- Keep files small and focused to make reviews and unit testing straightforward.
- Keep interfaces in the same folder as the implementations. Do not make explicit Interfaces folders.

## Testing

- Use FakeItEasy for fakes and mocks.
- Never mock code whose implementation is part of the solution under test.
- Prefer testing through public APIs; internal machinery may be tested via `InternalsVisibleTo` (ADR 0033).
- One behavior per test.
- Follow the Arrange-Act-Assert (AAA) pattern.
- Use clear assertions that verify the outcome expressed by the test name.
- Tests should be able to run in any order or in parallel.
- Assert specific values and edge cases, not vague outcomes.

## Build & test

- Build: `dotnet build Backtester.sln` (the human builds in-IDE with Ctrl+Shift+B; use the CLI to verify).
- Test: `dotnet test BacktesterTests\BacktesterTests.csproj`. Name the project explicitly —
  `BacktesterTests` is not in `Backtester.sln`, so a bare `dotnet test` resolves the solution, finds
  no test project, runs nothing, and still exits 0. A green bare `dotnet test` proves nothing.

## Practices

- Don't change TFM, SDK, or <LangVersion> unless asked.
- Use explicit usings.
- Nullable is disabled in the project.
- Check that everything compiles.
- Always add a comment to Dictionary declarations describing what the key and the value.
- Don't use one character variable names unless it's for simple and obvoius uses, like: for (int i = 0; i < 10; i++>).

## Agent skills

### Issue tracker

Issues live on GitHub Issues for this repository. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical triage labels follow the defaults: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Repository uses a single-context layout (single `CONTEXT.md` at repo root if present). See `docs/agents/domain.md`.