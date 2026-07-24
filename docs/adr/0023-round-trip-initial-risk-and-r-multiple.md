# Round trips carry initial risk so R-multiple is a first-class outcome

A trader thinks of a result in **R-multiples** — profit as a multiple of what was risked — but the
denominator, the entry stop, was thrown away. `RiskPerTradeSizing` reads `|entryPrice − stopPrice|`
to size the position and discards it; the armed bracket stop lived only on the broker's working
orders. The report already carried an `AvgRMultiple` labelled "Avg R", but it was a proxy —
`expectancy / |avgLoss|`, average realized loss standing in for risk — that does not reconcile with
any per-trade figure.

We decided that a round trip **carries its own initial risk as a stored primitive**, and R-multiple
is derived from it everywhere. The broker stamps the entry fill's declared stop onto the entry
`Trade` (a nullable `EntryStopPrice`, the same way it already stamps `Trade.Leg`); `Portfolio`
freezes the per-share stop distance on the `Position` when it opens from flat; each emitted
`RoundTrip` gets `InitialRisk = frozenStopDistance × exitedQuantity`. Both consumers derive the ratio
themselves — `PerformanceCalculator` for the (now redefined) "Avg R", `ReportModelBuilder` for the
new per-trip "R" column — so there is one stored number and no ratio to drift.

- **Initial risk** = per-share stop distance at entry × the round trip's quantity. Fixed at entry; a
  trailed stop moving later does not change it (consistent with the glossary's *Stop distance* vs
  *Stop level*). **R-multiple** = `RealizedPnL / InitialRisk`.
- The stop is **any declared entry stop**: the armed bracket `StopPrice` if the entry armed a
  bracket, else the entry `OrderRequest.StopPrice` sizing stop. R is defined for both bracketed and
  risk-sized entries; a round trip that opened from flat with no declared stop has `InitialRisk`
  null and shows no R.
- Anchored on the **opening** entry: scale-in adds do not re-blend the frozen stop distance, mirroring
  how `Portfolio` already freezes `EntryTime`/`EntryBarIndex` only at open-from-flat. A partial exit
  applies the same per-share distance to each exited slice, so slices have comparable R.

## Considered options

- *Redefine `AvgRMultiple` as the true mean of per-trade R (chosen)* vs. *keep the `expectancy /
  |avgLoss|` proxy and rename it* vs. *leave the proxy and add a non-reconciling per-trade column*.
  Chose to redefine: once real R exists per trip, the proxy has no independent value, and two things
  both called "R" that disagree is a reporting landmine. This is the reason the primitive must live
  on the domain `RoundTrip` — `PerformanceCalculator.Calculate` computes "Avg R" and has no access to
  the broker's `BracketLevelChanges` ledger.
- *Derive initial risk in the report only, from the first `StopLoss` `BracketLevelChange` in each
  trip's window*: rejected. It would split the "Avg R" computation out of `PerformanceCalculator`,
  cover only bracketed entries (not `RiskPerTradeSizing` signal-exit strategies), and duplicate risk
  logic outside the domain. Carrying the primitive on the round trip matches the precedent that
  **exit reason** is a derived fact stamped on the domain `RoundTrip`, not reconstructed downstream
  (ADR 0015).
- *Bracket-armed stop only* vs. *any declared entry stop (chosen)*: chose the broader rule so a
  strategy that risk-sizes and exits by signal still reports R — that trader is explicitly thinking
  in R and a blank column would be the surprising outcome.

## Consequences

- "Avg R" is now the mean of per-trade R over trips that **have** one; no-stop trips are excluded
  from both the numerator and the count (not counted as 0R, which would drag the mean toward zero).
  Its meaning changed from prior reports — the README metric description is updated accordingly.
- `Trade` gains a nullable `EntryStopPrice`, `Position` a frozen entry stop distance, and `RoundTrip`
  a nullable `InitialRisk`. The live-vs-report equivalence for round trips (ADR 0015) is preserved:
  the primitive is stamped on the entry fill and frozen at open-from-flat, so the value an
  `IRoundTripObserver` sees live equals the reported one — a future strategy could halt after N
  consecutive −1R trips.
- The report's round-trips table gains one "R" column after "Return %"; no separate initial-risk
  currency column. Undefined R renders as the em-dash used for rejected-attempt cells.
