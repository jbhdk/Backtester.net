# Portfolio emits round trips as a position reduces, making them a live, observable output

A strategy needs to react to the result of its own round trips while the run is in flight — the
motivating case is "stop entering after N losing round trips," but the seam is general (size down
after a win, pause after a streak, …). Today a round trip does not exist during the run: it is
reconstructed *after* the engine finishes by `PerformanceCalculator.BuildRoundTrips`, which re-pairs
the `Trade` ledger purely for the report. The strategy sees only `OnStart` and `OnBar`; nothing tells
it a position just closed, win or lose.

We decided the **`Portfolio` builds each `RoundTrip` the moment a reducing fill closes (or partially
closes) a position**, inside `ApplyTrade` where the realized PnL is already computed, and appends it
to `Portfolio.RoundTrips`. The `Position` gains the open lot's entry time and entry bar index
(captured when it opens from flat, using `_equityHistory.Count`), so the live round trip carries the
same `BarsHeld`, `EntryTime`, and `ExitReason` the post-hoc pass produced. The engine reads the round
trips closed during `ProcessBar` and delivers each — in close order, after the equity snapshot, before
`OnBar` — to any strategy implementing the opt-in `IRoundTripObserver` (`OnRoundTripClosed(RoundTrip)`),
mirroring the `IIndicatorSource` seam. `PerformanceCalculator.Calculate` now consumes
`Portfolio.RoundTrips`; **`BuildRoundTrips` is deleted**.

`Portfolio` thus becomes the single source of truth for round trips: one pairing implementation, no
risk of the live path and the report path diverging.

## Considered options

- *Keep `BuildRoundTrips` for the report and add a separate live detector in the engine*: rejected —
  two implementations of entry-to-exit pairing (average entry across scale-ins, partial exits,
  `BarsHeld`, exit reason) that must be kept byte-for-byte identical forever. The whole point of the
  feature is that the live result equals the reported result.
- *Put `OnRoundTripClosed` on `IStrategy`*: rejected — forces every implementer (including the raw
  `IStrategy` test double and any future one) to carry a method it ignores, against ISP and against
  the established `IIndicatorSource` precedent for optional capabilities.
- *Deliver the notification mid-fill, synchronously inside `ApplyTrade`*: rejected — interleaves
  strategy callbacks with fill processing (e.g. OCO sibling cancellation); the engine batches
  newly-closed trips and delivers them at one well-defined point in the loop instead.

## Consequences

- The live equivalence rests on two facts that hold today: `Position.AveragePrice` is unchanged by a
  reducing fill (so it equals the post-hoc running average), and overshoot is clamped in `ApplyTrade`
  *before* the trade is recorded (so the reversal branch in the old `BuildRoundTrips` was already
  unreachable). A future change that lets a single fill flip a position's sign would break this
  equivalence and must revisit the round-trip construction.
- The eight `BuildRoundTrips_*` unit tests move to drive `Portfolio.ApplyTrade` and assert on
  `Portfolio.RoundTrips` — testing through the public seam rather than a static helper.
- The engine tracks a high-water mark over `Portfolio.RoundTrips.Count` to find the trips closed on
  the current bar. A partial exit and a multi-symbol bar can each close more than one round trip per
  bar; each is delivered as its own `OnRoundTripClosed` call.
- The report is unchanged: it still reads round trips via `Portfolio.GetPerformanceStats`, and the
  `BracketLevelChange` slotting (ADR 0014) still matches on the round trip's `[EntryTime, ExitTime]`
  window, now sourced live but carrying identical values.
- The engine carries on regardless of what an observer does; acting on the result (halting entries,
  flattening, resizing) is entirely the strategy's concern. The seam informs; it does not control.
