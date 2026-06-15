# Agents.md

## Core principles

### Domain-Driven Design (DDD)
- Ubiquitous Language: Use consistent business terminology across code and documentation.
- Bounded Contexts: Clear service boundaries with well-defined responsibilities.
- Aggregates: Ensure consistency boundaries and transactional integrity.
- Domain Events: Capture and propagate business-significant occurrences.
- Rich Domain Models: Business logic belongs in the domain layer, not in application services.
### SOLID Principles
- Single Responsibility Principle (SRP): A class should have only one reason to change.
- Open/Closed Principle (OCP): Software entities should be open for extension but closed for modification.
- Liskov Substitution Principle (LSP): Subtypes must be substitutable for their base types.
- Interface Segregation Principle (ISP): No client should be forced to depend on methods it does not use.
- Dependency Inversion Principle (DIP): Depend on abstractions, not on concretions.
### .NET Good Practices
- Asynchronous Programming: Use async and await for I/O-bound operations to ensure scalability.
- Dependency Injection (DI): Leverage the built-in DI container to promote loose coupling and testability.
- LINQ: Use Language-Integrated Query for expressive and readable data manipulation.
- Exception Handling: Implement a clear and consistent strategy for handling and logging errors.
- Modern C# Features: Utilize modern language features (e.g., records, pattern matching) to write concise and robust code.

## Code style
- One file, one class.
- Use Gang of Four design patterns.
- Don't use var. Use explicit types and new().
- Comment all classes and public methods with clear and concise comments.
- All async methods end with Async.
- One file, one primary type: every public class, struct, enum, or interface gets its own .cs file named to match the declared type.
- Namespaces mirror folder/project names (e.g. `Backtester.Core`, `Backtester.Engine`).
- Tests follow the same rule: one test class per file; test file names mirror the production type (`PortfolioTests.cs` tests `Portfolio`).
- Keep files small and focused to make reviews and unit testing straightforward.
- Keep interfaces in the same folder as the implementations. Do not make explicit Interfaces folders.

## Testing

- Use FakeItEasy for fakes and mocks.
- Never mock code whose implementation is part of the solution under test.
- One behavior per test.
- Follow the Arrange-Act-Assert (AAA) pattern.
- Use clear assertions that verify the outcome expressed by the test name.
- Tests should be able to run in any order or in parallel.
- Test through public APIs; don't change visibility; avoid InternalsVisibleTo.
- Assert specific values and edge cases, not vague outcomes.

## Practices

- Don't change TFM, SDK, or <LangVersion> unless asked.
- Use explicit usings.
- Nullable is disabled in the project.
- Check that everything compiles.
- Always add a comment to Dictionary declarations describing what the key and the value.
- Don't use one character variable names unless it's for simple and obvoius uses, like: for (int i = 0; i < 10; i++>).