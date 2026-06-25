# Engine awareness of strategy-exposed indicator series

> Refined by [ADR 0012](0012-composite-indicator-multiple-series.md): what a strategy exposes is now a
> composite *Indicator* grouping one or more series in a shared pane, not a flat list of series.

To visualize the indicators a strategy actually used (in the HTML report), a strategy may now
*expose* named, time-aligned indicator series, and the engine collects them and surfaces them on
`BacktestResult`. The seam is the optional `IIndicatorSource` interface (implemented by
`StrategyBase`, with a `RecordIndicator` helper); exposure is opt-in, and `IStrategy.OnStart` is
unchanged. We chose awareness over the alternative of recomputing every series a second time in the
report, which would force the consumer to duplicate the exact computation the strategy already did.

This refines ADR 0003: the engine still ships **no** indicators and takes **no** indicator-library
dependency — it is indicator-agnostic *in computation*. Being aware of a series a strategy chose to
hand it is not a dependency. The line a future contributor must still not cross is bundling an
indicator library into the engine.
