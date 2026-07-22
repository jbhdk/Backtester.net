# Optimization sample

A complete, offline run of the **Optimizer**: it sweeps the two `[Optimize]`-annotated axes on
`MovingAverageCrossParameters` (the Fast and Slow moving-average periods) over four ETFs, runs the
Optimizer, and writes **two** reports:

- `optimization-report.html` — the leaderboard of every Trial ranked by Score, with the Score heatmap
  over the two swept axes.
- `winner-report.html` — the winning Trial's single-run report, built from `Best.BacktestResult`, with
  the winning Fast and Slow periods rendered as configuration cards.

This is a sample, not a test. It is not referenced by the test suite, and it needs no network access or
credentials.

## Running it

```bash
dotnet run --project samples/OptimizationSample
```

The bars come from the committed CSV files in [`samples/data`](../data), read through
`CsvHistoricalDataFetcher`, so the sweep is deterministic and needs no data provider. Both HTML files are
self-contained and open standalone from `file://`.

## What it demonstrates

The authoring path worth copying — see [`Program.cs`](Program.cs):

1. **Attributes-first authoring.** `Optimize.For(new MovingAverageCrossParameters(), factory)`
   `.FromAttributes()` reflects the `[Optimize]`-decorated Fast and Slow periods into the two swept axes.
2. **The `(ParameterSet, freshPortfolio) -> (strategy, broker)` factory.** `CreateTrial` builds a fresh
   moving-average cross strategy and broker for each Parameter set, reading the swept clone's periods
   directly.
3. **The two reports.** The Optimization report comes from `OptimizationReportModelBuilder` +
   `OptimizationHtmlReportWriter`; the winner's single-run report comes from `HtmlReportWriter`, with the
   winning Parameters carried back onto a `MovingAverageCrossParameters` instance so `[ReportSetting]`
   renders them as configuration cards. `[Optimize]` and `[ReportSetting]` co-decorate the same two
   properties without interacting.

## A winning Trial is the best *in-sample* configuration — not a validated one

Per [ADR 0020](../../docs/adr/0020-optimization-in-sample-grid-search.md), the Optimizer tunes and reports
Trials on the **same** data: there is no out-of-sample or walk-forward split. A winning Trial is therefore
the best *in-sample* fit, never evidence the strategy will hold up live. Overfitting is guarded only by the
minimum-trades **Eligibility** rule and by surfacing the **full ranked leaderboard** — so a winner's
neighbours reveal whether the peak is a plateau or a lucky spike — not by a train/test split.
