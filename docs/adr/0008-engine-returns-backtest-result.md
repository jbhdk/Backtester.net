# Engine returns a BacktestResult

`Engine.StartAsync` now returns a `BacktestResult` bundling the candle history, the portfolio, the
strategy's exposed indicator series, and the run inputs (symbols, interval, requested date range,
and starting equity), rather than only mutating caller-owned objects and discarding the fetched
history. This gives report generation a single source of truth for everything a run produced and
how it was configured: a report is built from the result alone, with no separately-supplied run
context, and the chart renders the exact bars the backtest ran on.

## Considered options

- **Expose the fetched history as a property on the engine** — smaller change and faithful to the
  existing "mutate caller-owned objects" pattern, but the property is only valid after a run
  (temporal coupling), and indicator series and portfolio would still be gathered from elsewhere.
- **Re-fetch candles in the report layer** — zero engine change, but re-does the fetch and risks
  diverging from what the engine actually ran on (freshness-window timing, range edges).

A returned result avoids the temporal coupling and keeps the run's outputs in one place, at the
cost of changing the `IEngine` contract.

## Amendment (ADR 0016)

"A report is built from the result alone" holds for everything the report *derives*. ADR 0016 adds
one deliberate exception: caller-declared **configuration cards** (`ReportModel.Configuration`) are
supplied by the caller and layered on top of the projection, because a strategy's settings do not
exist in a `BacktestResult` and cannot be derived. `ReportModelBuilder` still projects the result
alone and never populates that property.
