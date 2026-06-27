# Broker emits a bracket-level ledger so the report can draw stop/target lines

The report should draw a thin line on the price pane showing where each round trip's stop-loss and
take-profit sat, stepping with the level as a trailed stop moves. But a protective leg's price level
is lost downstream: the `Trade` ledger records *fills*, not the level a resting leg was armed at, nor
the `Modify` calls that trail it. Round trips, rebuilt far downstream in
`PerformanceCalculator.BuildRoundTrips`, never see it. Only the broker knows the level at each bar —
it owns the working orders whose `Price` *is* the level.

We decided the **broker appends a neutral `BracketLevelChange` ledger** — `{ Symbol, Time, Leg,
Price, OrderId }` — recorded when a protective leg is armed (its initial level) and on each `Modify`
of a known leg (its new level). The ledger is surfaced on `BacktestResult` alongside `RejectedOrders`,
and `ReportModelBuilder` projects it: each change-point is slotted into the round trip whose
`[EntryTime, ExitTime]` window contains it (same symbol; a symbol's round trips are non-overlapping,
so this is unambiguous), producing one stepped series per leg, clamped to the round trip's `ExitTime`.

This mirrors ADR 0013: the broker stamps neutral facts about execution; the report derives meaning.

## Considered options

- *Carry the level series on the `RoundTrip`*: rejected for the same reason ADR 0013 rejected a role
  map — the series would have to be threaded through `Portfolio.GetPerformanceStats` into
  `BuildRoundTrips`, coupling `Portfolio` to broker internals for a value the report can assemble
  itself from a fact-list the broker already has every reason to keep.
- *Derive levels from existing data*: impossible — arming levels and `Modify` events leave no trace
  in the `Trade` ledger or anywhere else; the information must be captured at the broker or not at all.

## Consequences

- The broker gains a second audit ledger (after `RejectedOrders`). `Modify` must consult `_legRoles`
  to record only protective-leg moves and ignore plain-order modifications.
- A Signal exit does not cancel the resting bracket legs in the current broker, so the report must
  clamp each round trip's series to its `ExitTime` rather than trusting the legs to stop emitting.
- One active bracket per position is assumed for rendering. `OrderId` is carried on each change-point
  so concurrent brackets (scaling in) can be split per-order later without a data migration; until
  then, concurrent brackets within one round trip may draw oddly — a documented limitation.
