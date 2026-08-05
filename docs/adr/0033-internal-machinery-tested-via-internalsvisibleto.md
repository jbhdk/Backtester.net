# Internal machinery stays internal and is tested via InternalsVisibleTo

Extracting a `Bracket` type out of `BrokerSimulator`'s six correlated dictionaries raised a question
the codebase had been answering by prohibition rather than by reasoning: a type that exists only so
the broker can do its job correctly is not something a strategy author should ever name, yet the
only way to test it directly was to publish it into `backtester.net`'s package contract. We decide
the general rule instead of the one case: **engine-internal machinery stays `internal`, and tests
reach it through `InternalsVisibleTo`. A type goes public when a consumer outside the assembly needs
it — a production consumer, not a test.**

**The package contract is the thing being protected.** `Backtester` ships as `backtester.net`, so
every public type is a promise about shape that a version bump has to honour. `Bracket` is a state
machine we expect to reshape as bracket semantics grow (partial exits, several brackets per symbol,
per-leg quantities are all plausibly next). Public would freeze it for the benefit of nobody: a
strategy author submits a `BracketRequest` and holds a `BracketHandle`, both of which stay public,
and never has occasion to name the machine that connects them. The same reasoning runs the other way
for `Warmup`, which is `internal` today and which the Optimizer — a *production* consumer in another
assembly — genuinely needs. That is the line: **who needs it, not how convenient it is to test.**

**Widening visibility for a test is not the same as designing for a test.** The rejected alternative
is not "make it public"; it is "reach it only through the type that owns it." That is what makes
`Bracket` untestable in isolation today: a bracket assertion has to drive `ProcessBar` with a
`MarketSlice`, a `Portfolio`, and a fill model in order to observe one offset resolution. The test
that pays is not a unit-purity exercise — it is that a bracket bug currently has to be reproduced
through four collaborators before it can be seen.

**This does not license a mocking seam.** An `internal` type is still solution code, so the standing
rule against faking implementations that live in this solution applies to it unchanged. An interface
introduced so a collaborator can be faked remains rejected on
[ADR 0031](0031-currency-converter-module.md)'s own grounds — a seam that exists only to be mocked —
and that rejection is *strengthened* here, not weakened: with `InternalsVisibleTo` available, the
concrete type is directly reachable, so the last practical argument for such an interface is gone.

## What this supersedes in ADR 0031

ADR 0031 rejected **"`ObserveRate` internal, with `InternalsVisibleTo` for tests"** on the grounds
that "the codebase tests through public APIs and does not widen visibility for tests," and made
`CurrencyConverter.ObserveRate` public for that reason alone. That rejection is **overturned**. It
rested on a `CLAUDE.md` rule — *"Test through public APIs; don't change visibility; avoid
`InternalsVisibleTo`"* — which has since been removed, so the premise no longer holds; the ADR now
reads as a conclusion whose reason is gone.

`ObserveRate` is nonetheless **left public and unchanged**. Narrowing it is a currency-module edit
with no bracket motivation, and it would put an unrelated behaviour change inside a broker refactor.
It is the known follow-on this ADR deliberately does not take, recorded here so the contradiction
between 0031's public member and this policy is a decision on the shelf rather than an oversight.
Everything else in ADR 0031 stands, including its rejection of an `ICurrencyConverter` interface,
which this ADR upholds and extends.

## Considered options

- **Make `Bracket` public in `Backtester.Broker`.** Rejected: it enters the package contract and is
  frozen by versioning for the benefit of no caller, while a sibling review item is simultaneously
  arguing the 66-public-type core is already too wide. Discoverability is not a benefit when the
  thing discovered is machinery a strategy must not touch.
- **Keep `Bracket` internal and test it only through `BrokerSimulator`.** Rejected: it preserves the
  exact coupling the extraction exists to remove. A test would still need `ProcessBar`, a
  `MarketSlice`, and a `Portfolio` to assert that a long entry filling at 100 with a stop offset of 2
  places a Sell stop at 98.
- **A callback interface (`ILegPlacer`) so `Bracket` places its own legs against a fake.** Rejected
  twice over: it is a one-implementation, one-caller interface of exactly the kind being deleted
  elsewhere, and testing through it yields interaction assertions against a fake instead of state
  assertions against a return value. `Arm(fill, side)` returning leg descriptions needs no seam at
  all.
- **A blanket rule that everything a test needs to see becomes public.** Rejected: that is the
  status quo by another name, and it lets test convenience author the published contract.

## Consequences

- `Backtester` gains its first `[assembly: InternalsVisibleTo("BacktesterTests")]`. It must be a
  hand-written attribute in a `.cs` file: `Backtester.csproj` sets `GenerateAssemblyInfo=false`, so
  the MSBuild `<InternalsVisibleTo>` item is silently inert.
- The `CLAUDE.md` testing rules no longer say "test through public APIs." The discipline it protected
  survives as this ADR's line — visibility follows who *needs* the type — rather than as a
  prohibition, so the next "should this be public?" has a rule to answer with instead of a habit.
- A test asserting on an `internal` type is coupled to a shape no consumer depends on, so a refactor
  can break tests without breaking any caller. Accepted deliberately: that is the cost of testing a
  concept before its contract has settled, and it is cheaper than freezing the contract to find out.
- `CurrencyConverter.ObserveRate` stays public though this policy no longer requires it, as recorded
  above.
- The policy pre-answers the `Warmup` question a pending review item raises: publish it, because the
  Optimizer needs it in production — not because it is hard to test.
