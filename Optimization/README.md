# backtester.net.optimization

Parameter optimization for the [backtester.net](https://www.nuget.org/packages/backtester.net) engine.

An **Optimizer** sweeps a strategy's **Parameters** over a grid (each Parameter given a range and step),
runs a full backtest per combination (a **Trial**), and ranks the resulting Trials by an **Objective**.
The produced **Optimization** carries every Trial's Performance stats and Score, flags the Trials that are
ineligible to win, and exposes the best Trial's full `BacktestResult` so the existing single-run report
can be produced for the winner.

This package references `backtester.net` and `backtester.net.report` and nothing else. It **makes no
outbound call**: the Optimizer re-runs the existing `Engine` unchanged, once per Trial, over bars fetched
once and shared across Trials.

## Authoring: attributes-first

The documented lead is **attributes-first**. Mark the Parameters to vary with `[Optimize(min, max, step)]`
(`int` and `decimal` take a range; a `bool` uses the parameterless `[Optimize]` and expands to
`{ false, true }`), and supply one factory that builds a Trial's strategy **and** broker from a Parameter
set. Because the factory builds the broker too, a Parameter feeding an execution model (e.g.
`RiskFraction` → `RiskPerTradeSizing`) actually takes effect instead of being a silent no-op.

```csharp
using Backtester.Optimization;
using Backtester.Report.Toolkit;   // optional: [ReportSetting] for the winner's configuration cards

public class MaCrossParameters
{
    [Optimize(5, 15, 5)]                          // 5, 10, 15
    [ReportSetting("Fast period", "Strategy")]    // co-decorates without interacting
    public int FastPeriod { get; set; }

    [Optimize(20, 40, 10)]                         // 20, 30, 40
    [ReportSetting("Slow period", "Strategy")]
    public int SlowPeriod { get; set; }
}

// The [Optimize] properties become the swept axes; the factory realizes each Parameter set into a
// fresh strategy and broker. The factory receives a strongly-typed clone with the swept properties set,
// so it reads them directly (parameters.FastPeriod).
OptimizationSetup setup = Optimize
    .For(new MaCrossParameters(), (parameters, portfolio) =>
        (new MovingAverageCrossStrategy(parameters.FastPeriod, parameters.SlowPeriod),
         new BrokerSimulator(portfolio)))
    .FromAttributes();
```

`[Optimize]` is orthogonal to the report toolkit's `[ReportSetting]`: the two co-decorate a property
without interacting, so a Parameter can be both varied and displayed.

## Running the Optimizer

The Optimizer owns the shared run inputs (fetcher, symbols, date range, interval) and hands the factory a
fresh `Portfolio` per Trial, so one Trial's positions or cash never leak into the next.

```csharp
Optimizer optimizer = new Optimizer(
    fetcher,
    symbols: new[] { "SPY", "QQQ" },
    fromUtc: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    toUtc:   new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    interval: "1d",
    portfolioFactory: () => new Portfolio(100_000m),
    setup,
    objective: Objectives.Sharpe,   // the default
    minimumTrades: 30);             // Eligibility floor

// Trials run in parallel; progress is reported once per completed Trial, and the token stops the sweep.
Progress<OptimizationProgress> progress = new(update =>
    Console.WriteLine($"Trial {update.Completed}/{update.Total}"));

OptimizationResult result = await optimizer.RunAsync(progress, cancellationToken);
```

The Optimizer fetches each symbol **once**, wraps the bars in an in-memory `IHistoricalDataFetcher`
shared read-only across every Trial, and runs Trials in **parallel** via `Parallel.ForEachAsync` — yet
collects results in Parameter-space order, so a parallel sweep ranks identically to a sequential one.

## Priming across runs (in-sample + out-of-sample)

A single sweep already fetches each symbol once — priming is not needed *within* a sweep. It pays off
across **several** sweeps over the same instruments: run an in-sample sweep and then a separate
out-of-sample sweep (or re-run with different ranges) without re-hitting the network for each. Pass a
cache-aware `HistoricalDataFetcher`, **prime** the wide range once up front with `IDataPrimer`, then run
each window over sub-ranges served entirely from the warm Cache:

```csharp
HistoricalDataFetcher fetcher = new(new YahooHistoricalDataProvider(), dataFolder: "data");

// Warm the whole span once — no backtest runs here.
await fetcher.PrimeAsync(new[] { "SPY", "QQQ" },
    new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow, "1d");

// In-sample sweep, then out-of-sample sweep — both read the warm Cache, no further network calls.
// buildOptimizer is your own factory that news up an Optimizer over the given [from, to] window.
OptimizationResult inSample = await buildOptimizer(fetcher, new DateTime(2020, 1, 1), new DateTime(2022, 1, 1)).RunAsync();
OptimizationResult outOfSample = await buildOptimizer(fetcher, new DateTime(2022, 1, 1), new DateTime(2024, 1, 1)).RunAsync();
```

The Optimizer **inherits the coverage guard for free**: it fetches through `IHistoricalDataFetcher`, so a
window whose start precedes what has been fetched (or primed) fails the sweep loudly with a
`DataCoverageException` rather than sweeping on a silently short slice. Priming is a **caller** step — the
Optimizer's API is unchanged. See [ADR 0021](https://github.com/jbhdk/Backtester.net/blob/main/docs/adr/0021-coverage-floor-and-priming.md).

> The Optimizer itself is still **in-sample** (ADR 0020): it does not split train/test or validate a
> winner out-of-sample. Priming just makes running your own separate out-of-sample sweep cheap and safe;
> a built-in walk-forward seam is deferred.

## Objective, Score, and Eligibility

- **Objective** — the rule Trials are ranked by: a function over the combined (whole-run)
  `PerformanceStats` paired with a maximise/minimise direction. It reads combined stats only, never
  Per-symbol stats. Build one with `Objective.Maximize(stats => stats.Sharpe)` /
  `Objective.Minimize(stats => stats.MaxDrawdown)`, or use a preset from `Objectives`:
  `Sharpe` (the default), `NetProfit`, `Calmar`, `MinDrawdown`, `ProfitFactor`.
- **Score** — the number the Objective assigns a Trial. Trials are ranked by it, best first.
- **Eligibility** — a configurable minimum Round-trip count (`minimumTrades`). A Trial below the floor is
  **ineligible to win** — it can never be `Best` — but is still ranked and shown, flagged, so a degenerate
  low-trade Trial cannot win on a lucky Score, and nothing is ever silently dropped.

## The result

`OptimizationResult` carries every `Trial` (its Parameter set, key `PerformanceStats`, `Score`, and
eligibility flag) ranked best-first, and `Best` — the winning eligible Trial, or `null` when no Trial met
the floor. The winner always carries its full `BacktestResult`; retaining **every** Trial's result is an
opt-in flag (`retainAllBacktestResults: true`) so a small sweep can keep them all while a large sweep
stays memory-bounded by default.

```csharp
Trial best = result.Best;
Console.WriteLine($"Fast {best.Parameters.Int("FastPeriod")}, Slow {best.Parameters.Int("SlowPeriod")} " +
    $"— Sharpe {best.Score:F2} over {best.Stats.Trades} trades");
```

## Reporting

The Optimization report renders the whole sweep as a sortable leaderboard — best highlighted, ineligible
Trials flagged — with a Score **heatmap** when exactly two Parameters vary (per-Parameter marginals
otherwise). A pure `OptimizationReportModelBuilder` projects the result into a serializable model (the
render types live in `backtester.net.report`, so that package never depends on this one), and a thin
`OptimizationHtmlReportWriter` inlines it into one self-contained HTML file.

```csharp
OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);
new OptimizationHtmlReportWriter().Write(model, "optimization.html");
```

There is **no automatic winner drill-down**: you render the winner's single-run report yourself from
`Best.BacktestResult`, where `ConfigurationCardBuilder` already turns `[ReportSetting]` into cards.

```csharp
MaCrossParameters winning = new()
{
    FastPeriod = result.Best.Parameters.Int(nameof(MaCrossParameters.FastPeriod)),
    SlowPeriod = result.Best.Parameters.Int(nameof(MaCrossParameters.SlowPeriod))
};
IReadOnlyList<ReportCard> cards = new ConfigurationCardBuilder().Build(winning);
new HtmlReportWriter().Write(result.Best.BacktestResult, "winner.html", cards);
```

## Two more authoring paths

Both compile to the same primitive underneath — a public `ParameterSpace` plus a Trial factory — so you
can keep the search space where it belongs:

```csharp
// Fluent .Vary(): keep the ranges in the experiment rather than annotating the strategy.
OptimizationSetup setup = Optimize
    .For(new MaCrossParameters(), factory)
    .Vary(parameters => parameters.FastPeriod, 5, 15, 5)
    .Vary(parameters => parameters.SlowPeriod, 20, 40, 10)
    .Build();

// Explicit ParameterSpace: for a bare-constructor strategy with no parameters class.
ParameterSpace space = new ParameterSpace()
    .AddInt("fast", 5, 15, 5)
    .AddInt("slow", 20, 40, 10);
OptimizationSetup explicitSetup = new OptimizationSetup(space, (set, portfolio) =>
    (new MovingAverageCrossStrategy(set.Int("fast"), set.Int("slow")), new BrokerSimulator(portfolio)));
```

An `[Optimize]` (or `.Vary`) Parameter the factory never consumes is **rejected**: an axis that would
silently produce identical Trials fails loudly instead of quietly wasting the sweep.

## Scope

v1 is **in-sample grid search** ([ADR 0020](https://github.com/jbhdk/Backtester.net/blob/main/docs/adr/0020-optimization-in-sample-grid-search.md)):
the Optimizer tunes and reports on the same data, with no out-of-sample or walk-forward validation. **A
winning Trial is the best *in-sample* configuration, not a validated one.** Overfitting is guarded only by
the minimum-trades **Eligibility** rule and by surfacing the full ranked leaderboard — so a winner's
neighbours reveal whether the peak is a plateau or a lucky spike — not by a train/test split. Non-grid
search methods (random, coarse-to-fine, Bayesian) and the seam that would host them are deferred, added
deliberately when a real second method lands.

## Example

A complete, offline example lives in
[`samples/OptimizationSample`](https://github.com/jbhdk/Backtester.net/tree/main/samples/OptimizationSample):
it sweeps two `[Optimize]` axes over committed CSVs via `CsvHistoricalDataFetcher`, writes the
Optimization report and the winner's single-run report, and runs with
`dotnet run --project samples/OptimizationSample` — no network access or credentials.

## Documentation

- [ADR 0020](https://github.com/jbhdk/Backtester.net/blob/main/docs/adr/0020-optimization-in-sample-grid-search.md) — Optimization is in-sample grid search
