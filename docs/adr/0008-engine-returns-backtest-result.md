# Engine returns a BacktestResult

`Engine.StartAsync` now returns a `BacktestResult` bundling the candle history, the portfolio, and
the strategy's exposed indicator series, rather than only mutating caller-owned objects and
discarding the fetched history. This gives report generation a single source of truth for
everything a run produced and guarantees the chart renders the exact bars the backtest ran on.

## Considered options

- **Expose the fetched history as a property on the engine** — smaller change and faithful to the
  existing "mutate caller-owned objects" pattern, but the property is only valid after a run
  (temporal coupling), and indicator series and portfolio would still be gathered from elsewhere.
- **Re-fetch candles in the report layer** — zero engine change, but re-does the fetch and risks
  diverging from what the engine actually ran on (freshness-window timing, range edges).

A returned result avoids the temporal coupling and keeps the run's outputs in one place, at the
cost of changing the `IEngine` contract.
