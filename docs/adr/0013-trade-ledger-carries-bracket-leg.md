# Trade ledger carries the bracket-leg role so exit reason is recoverable

The report's round-trip list should say *why* each round trip closed: at its take-profit, at its
stop-loss, or by a plain strategy order (a **Signal** exit). Only the broker knows this — at fill
time it knows whether the filling order was a bracket's stop leg, its target leg, or neither. But
round trips are rebuilt far downstream in `PerformanceCalculator.BuildRoundTrips`, which sees only
the `Trade` ledger and the equity history; it has no reference to the broker, its OCO links, or even
the order `Type`. The classifying knowledge is discarded the moment the `Trade` is produced.

We decided the **broker stamps each `Trade` with a neutral `BracketLeg` role** (`None`, `StopLoss`,
`TakeProfit`), recorded when the leg is armed and copied onto the fill. The three-value **Exit
reason** (`TakeProfit`, `StopLoss`, `Signal`) is then *derived on the `RoundTrip`* from its exit
trade's leg — `None` on an exit becomes `Signal`. The leg stays a neutral fact about a fill (an
entry fill also carries `None`); "Signal" is meaningful only for an exit, so it lives on the round
trip, not the trade.

## Considered options

- *Broker-side `orderId → role` map passed into `BuildRoundTrips`*: rejected — the `Trade` already
  flows broker → `Portfolio` → `PerformanceCalculator`, but a role map does not. It would force the
  map through `Portfolio.GetPerformanceStats`/`GetPerformanceStatsBySymbol`, making `Portfolio`
  depend on broker internals for a value the ledger could simply carry.
- *Store the final `ExitReason` (with `Signal`) directly on the `Trade`*: rejected — `Signal` is not
  a property of a fill; an entry fill and a manual-close fill are indistinguishable at the trade
  level (both non-bracket). Conflating the two would mislabel entries.

## Consequences

- A `Trade` (a Core DTO, a passive record of one fill) now carries one fact about the order that
  produced it — the bracket leg that armed it. This is the headline cost: the fill record gains a
  sliver of bracket awareness.
- Exit reason is available to any consumer of round trips without new plumbing, and the report gets
  its "Exit" column for free once the leg is stamped.
- The leg is recorded only for bracket legs the broker arms; should non-bracket protective orders
  ever be added, they would default to `None` (Signal) until taught otherwise.
