# Indicators are composite: multiple series in one shared pane

> Refines [ADR 0007](0007-engine-indicator-awareness.md): the engine surfaces composite *indicators*,
> each grouping one or more series, rather than a flat list of independent series.

To visualize indicators like MACD — which is one indicator drawn as three plots (the MACD line, the
signal line, and a histogram) sharing a single pane — an exposed **Indicator** now groups one or more
**Indicator series** under a single name and a single placement (price overlay, or its own separate
pane that all of its series share). Each series declares its own shape (line, area, or histogram); the
placement and symbol binding moved up from the series to the Indicator. A single-line indicator (e.g.
a moving average) is just an Indicator with one series, so the common case stays simple. The engine
collects `IIndicatorSource.Indicators` and surfaces them on `BacktestResult`; the report renders one
separate pane per Indicator.

This stays inside ADR 0003: the engine still ships no indicators and takes no indicator-library
dependency. A strategy computes MACD (or any multi-series study) itself and hands over the finished
series; the engine and report only carry and draw them.

## Considered options

- **Flat series with a shared pane key** — keep the flat list of independent series and add an
  optional pane-group key so series with the same key land in one pane. Rejected: it leaves "which
  series belong together" as an implicit join on a string key, and keeps placement on each series even
  though every series in a group must agree on it. The composite aggregate makes the grouping and the
  single shared placement explicit and unforgeable.

## Consequences

- Series shape (line / area / histogram) is now declared on the series rather than inferred from its
  placement. Shape is treated as structural; colour and line width remain render-time concerns derived
  by the report (e.g. the histogram's four-colour sign-and-momentum fill and the zero reference line
  are computed when rendering, not stored on the model).
