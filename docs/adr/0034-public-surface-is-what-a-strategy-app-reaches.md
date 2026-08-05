# The public surface is what a strategy app can reach, not what the solution uses

[ADR 0033](0033-internal-machinery-tested-via-internalsvisibleto.md) settled that visibility follows
who *needs* a type, and named the test: "a consumer outside the assembly needs it — a production
consumer, not a test." Applying that test to `Backtester`'s 72 public types exposed the ambiguity it
left behind. Nine sibling packages — `Report`, `Optimization`, `Stops`, the `Data.*` providers, the
`Analysis` pair — all reference `Backtester` across an assembly boundary and are all production. Read
literally, that clause keeps roughly 57 of the 72 public and makes the rule vacuous.

We decide the missing half: **a consumer is someone writing a strategy application against the
packages. A sibling `backtester.net` package is not a consumer — it is this library's own internals,
spread across assemblies for packaging reasons.** A type or member is public when a strategy app can
reach it: because the app's own source names it, or because it is reachable transitively through the
signature of something the app calls. Everything else is `internal`.

**Reachability, not usage, is the test.** Counting references would have been wrong in both
directions. `BracketLegSpec` appears in no app's source yet is a required constructor parameter of
`BracketRequest`; `IIndicatorSource` and `IRoundTripObserver` appear in none because `StrategyBase`
already implements them and every app derives from it; `OptimizeAttribute` appears sixteen times but
only as `[Optimize(...)]`. Meanwhile `Portfolio.ApplyTrade`, `RecordEquitySnapshot`, `SnapshotAt`,
`InitialMarginForOrder`, `ValuationPriceForOrder` and `ReducesOpenPosition` are used by nothing
outside the engine at all — every one of them account-mutating or broker-internal, and every one of
them published today.

**A strategy author's test project is part of that author's application.** `Test1`'s suite fakes
`IBroker` and writes `new BracketHandle { StopOrderId = … }`; several apps construct `Candle`
directly. Testing a strategy in isolation *means* faking the broker and fabricating what it returns,
so a DTO the engine hands to a strategy stays fabricable by that strategy's author. This is not the
rule ADR 0033 rejected — that was *our* tests authoring *our* contract. This is a consumer need that
happens to arise in a consumer's tests, and the who-needs-it test answers it the same way it answers
any other.

**`IBrokerSimulator` is deleted**, and this is the one shape change the pass takes. It is a
one-implementation interface whose sole cross-assembly role is as a token: a strategy app's trial
factory returns one, `Optimizer` passes it to `Engine`, and `Optimizer` never calls a method on it.
Yet by being public it publishes `ProcessBar(MarketSlice)` and `SubmitOrder(OrderRequest)` — the exact
engine plumbing this ADR exists to hide — and drags `MarketSlice` public behind them. An interface
cannot have internal members, so the interface itself had to go. `Engine`'s constructor and
`Optimization`'s trial-factory delegate take the concrete `BrokerSimulator`. No strategy app changes:
they all write `(strategy, new BrokerSimulator(…))` and let the tuple convert implicitly.

**Samples are documentation, not evidence.** `samples/` must compile against whatever survives, and
it does not justify keeping anything: the only thing needing `IEngine` was one stylistically
interface-typed local, which becomes `Engine engine = new(…)` as all five real apps already write it.

## Considered options

- **Sibling packages count as consumers.** Rejected: faithful to ADR 0033's literal words and needs no
  friend-assembly wiring, but it leaves the surface essentially unchanged. The measurement is what
  killed it — only four members are wanted by a sibling and by no app.
- **Types only, leaving members alone.** Rejected: the dangerous surface is on types that must stay
  public. `Portfolio` is constructed by every app, so a type-level sweep leaves `ApplyTrade` and a
  mutable `List<Position>` published regardless.
- **Also take the shape fixes** — `IReadOnlyList<Position>`, sealing, collapsing
  `SubmitOrder`/`Submit`. Rejected for this pass on ADR 0033's own reasoning about `ObserveRate`: an
  unrelated behaviour change inside a visibility sweep. `IBrokerSimulator` is the deliberate exception
  because nothing else unlocks a comparable amount and its measured cost is four lines of samples.
- **`InternalsVisibleTo` for all nine siblings, uniformly.** Rejected: it hands every sibling the
  whole internal surface, so nothing stops `Report` reaching into `Bracket` next. The grant is
  `Report` and `Optimization` only, for four named members.
- **Keep those four members public instead of granting IVT.** Rejected: it freezes
  `GetPerformanceStats`, `GetPerformanceStatsBySymbol`, `EquityHistory` and `ConversionSymbols` into
  the contract for a third-party report package that does not exist. Widening later is
  non-breaking; narrowing later is not.
- **A type documented in `CONTEXT.md` stays public.** Rejected: `CONTEXT.md` names concepts, not .NET
  types. Bracket and Slice are both in it and both belong `internal` — the rule would re-publish
  exactly what ADR 0033 hid.

## Consequences

- `backtester.net` and `backtester.net.optimization` go to **2.0**. This is a binary-breaking change;
  the major is the only honest signal, and it follows `backtester.net.stops`, already at 2.0. The
  other eight packages stay on 1.0 rather than claim a break that did not happen. Every strategy app
  pins an exact version, so each opts in when ready instead of being broken in place.
- A type may be kept public **only** by naming the shipped capability it serves, written down at the
  time. `Instrument` and `CurrencyConverter` stay because forex is a shipped, documented feature
  (ADR [0029](0029-instrument-and-multi-currency-forex-accounting.md),
  [0030](0030-forex-margin-via-per-instrument-leverage.md)) that none of the five apps happens to use;
  `CsvBarLoader` and `CsvHistoricalDataFetcher` stay because offline runs are a real capability.
  `PerformanceCalculator`, `OrderStopDistance`, `IEngine`, `CoverageFloorLoader` and
  `AtrBracketStrategy` have no such sentence and go `internal`. The burden of proof is on staying
  public.
- Two of the closure's named types resisted, and the compiler settled both — as this ADR said it
  would. `FillResult` **stays public**: it is the element type of `IFillModel.DetermineFills`, so
  narrowing it is a `CS0050` that would take the Fill Execution model's own interface internal with
  it. That is reachability, the rule above, arriving at the opposite answer from the closure — and
  reachability wins. `PositionMetadata` was **deleted instead**: an empty class with no members,
  reachable only through `Position.Metadata`, a property no strategy app, sibling package, sample or
  test ever read or wrote. `OrderRequest.ClientMetadata` is the strategy-metadata seam that actually
  works. Internalizing an empty unused type would only have hidden dead code behind a keyword; the
  2.0 major was already being taken, so removal cost no extra signal.
- Two seams survive on reachability rather than on the allowlist, and both are easy to get wrong.
  `IWarmupResolvingFetcher` is a parameter type on `Engine`'s *public* constructor — an app passing a
  `HistoricalDataFetcher` into it needs the parameter type accessible — so it stays public despite no
  app naming it. `IDataPrimer` is named in the prose of `DataCoverageException` and
  `InsufficientWarmupBarsException` ("Prime an earlier range via `IDataPrimer.PrimeAsync`"); it goes
  `internal` only if those messages are reworded to name `HistoricalDataFetcher.PrimeAsync`, since an
  error must not point at a type the reader cannot see.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` with a `PublicAPI.Shipped.txt` baseline makes any new
  public member a build error until it is added deliberately. The surface reached 72 types because
  nothing stopped it; an ADR alone would not have, since none existed to be ignored.
- Verification is a real compile, not a search: the solution is Release-built so 2.0 reaches
  `C:\Source\NugetRepo`, then `Test1` — the richest consumer, using optimization, brackets, trailing
  stops and broker fakes — is bumped and built with its tests. The closure was derived by searching
  source, and searching source is exactly what misreads `[Optimize(...)]`.
- Only `Backtester` is audited in this pass. The rule above is the standing policy for the other nine,
  which must be re-checked anyway once `Backtester` narrows.
