# backtester.net

A bar-by-bar backtesting engine for financial market strategies, written in C# on **.NET 8**.

It steps through historical candles one bar at a time, lets a strategy emit orders, simulates
broker fills with pluggable execution models, and tracks a portfolio and its performance — the same
rhythm as a live trading loop, but deterministic and replayable. An optional companion library turns
each run into a self-contained, interactive HTML report.

The engine is **indicator-agnostic**: it ships none and takes no indicator dependency. Your strategy
brings its own library, computes its series, and acts on them.

---

## Packages

The repository builds eight NuGet packages. Use the engine on its own, and add a data source,
reporting, analysis, or optimization when you want them. The core makes no outbound network call on its own — every live data
provider is an opt-in package (see [ADR-0009](docs/adr/0009-network-providers-separate-packages.md)).

| Package | What it is | Depends on |
|---|---|---|
| [`backtester.net`](Backtester/README.md) | The backtesting engine: the data seams, the cache-aware fetcher, the offline CSV provider, broker, portfolio, strategies, execution models. | — |
| [`backtester.net.yahoo`](Backtester.Data.Yahoo/README.md) | Opt-in Yahoo Finance data provider. BCL-only. | `backtester.net` |
| [`backtester.net.alpaca`](Backtester.Data.Alpaca/README.md) | Opt-in Alpaca data provider (US equities, consolidated SIP, split-adjusted). | `backtester.net`, `Alpaca.Markets` |
| [`backtester.net.report`](Report/README.md) | Opt-in HTML reporting built from a run's `BacktestResult`. Kept separate so the engine takes on no web-asset dependencies. | `backtester.net` |
| [`backtester.net.report.toolkit`](Report.Toolkit/README.md) | Opt-in report-side helper that reflects an attributed settings object into configuration cards, so a strategy's parameters render at the top of the report. | `backtester.net.report` |
| [`backtester.net.analysis`](Analysis/README.md) | Opt-in, AI-agnostic Analysis: reduces a report model to an Analysis digest, asks an Analysis client, and enforces the Analysis contract. Makes no outbound call. | `backtester.net.report` |
| [`backtester.net.analysis.claude`](Analysis.Claude/README.md) | Opt-in Claude Analysis client. All the network code for the analysis feature lives here. | `backtester.net.analysis`, `Anthropic` |
| [`backtester.net.optimization`](Optimization/README.md) | Opt-in Parameter Optimization: sweeps a strategy's Parameters over a grid, runs a backtest per combination, and ranks the Trials by an Objective, with a sortable leaderboard report. In-sample grid search (ADR 0020). Makes no outbound call. | `backtester.net`, `backtester.net.report` |

---
<!-- 
## Installation

From [NuGet](https://www.nuget.org/):

```bash
# The engine
dotnet add package backtester.net

# Optional: HTML reporting (pulls in the engine transitively)
dotnet add package backtester.net.report
```

Or via the Package Manager console:

```powershell
Install-Package backtester.net
Install-Package backtester.net.report
``` -->

### Building from source

```bash
git clone https://github.com/<your-org>/backtester.net.git
cd backtester.net
dotnet build         # builds the engine, report library, and tests
dotnet test          # runs the test suite
```

Requires the **.NET 8 SDK** or later.

---

## Quick start

A complete run: fetch data, simulate a strategy, and write an HTML report.

```csharp
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Data.Yahoo;   // from the backtester.net.yahoo package
using Backtester.Engine;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Slippage;
using Backtester.ExecutionModels.Sizing;
using Backtester.Strategies;
using Backtester.Report;

// 1. Pick a strategy (or write your own — see below).
IStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 10, slowPeriod: 50);

// 2. Wire up the portfolio and broker. Every execution model is optional;
//    omit one and the broker applies a sensible default (or none).
Portfolio portfolio = new Portfolio(100_000m);
IBrokerSimulator broker = new BrokerSimulator(
    portfolio,
    commissionModel: new PerShareCommission { PerShare = 0.005m },
    slippageModel:   new FixedSlippage { Amount = 0.01m },
    sizingModel:     new FixedSizeModel { FixedSize = 100 });

// 3. Create a cache-aware data fetcher. It serves bars from a local CSV cache
//    and only calls Yahoo Finance for bars the cache is missing. The Yahoo
//    provider ships in the opt-in backtester.net.yahoo package.
IHistoricalDataFetcher fetcher = new HistoricalDataFetcher(new YahooHistoricalDataProvider());

// 4. Run the engine. It fetches each symbol, synchronizes them into slices,
//    and steps bar by bar.
IEngine engine = new Engine(
    fetcher,
    symbols:  new[] { "AAPL", "MSFT" },
    fromUtc:  new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    toUtc:    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    interval: "1d",
    strategy,
    broker,
    portfolio);

BacktestResult result = await engine.StartAsync();

// 5. Inspect the numbers...
PerformanceStats stats = result.Portfolio.GetPerformanceStats();
Console.WriteLine($"Net profit:   {stats.NetProfit:C}");
Console.WriteLine($"Win rate:     {stats.WinRate:P1}");
Console.WriteLine($"Profit factor:{stats.ProfitFactor:F2}");
Console.WriteLine($"Max drawdown: {stats.MaxDrawdown:C}");

// 6. ...and write an interactive HTML report you can open in any browser.
new HtmlReportWriter().Write(result, "report.html");
```

`StartAsync` returns a [`BacktestResult`](Backtester/Engine/BacktestResult.cs) — the single source
of truth for a run. It bundles the exact candle history the engine stepped through, the final
portfolio, the run inputs (symbols, interval, date range, starting equity), and any indicator series
the strategy exposed. A report is produced from this result alone.

---

## Core concepts

The engine has a precise vocabulary — the full glossary lives in [`CONTEXT.md`](CONTEXT.md). The
essentials:

- **Bar / Candle** — one OHLCV interval. The engine advances one bar at a time.
- **Slice** — all symbols' bars at a single timestamp; the unit the engine processes per step.
- **Next-bar fill** — an order submitted while processing bar _N_ is evaluated against bar _N+1_.
  This is the engine's anti-lookahead rule: a strategy can never trade on information it would not
  yet have had.
- **Order** — a resting instruction (Market, Limit, or Stop) that persists across bars until filled
  or cancelled (GTC). A **bracket** is an entry plus an attached stop-loss and take-profit that form
  an OCO group.
- **Position** — the net holding in a symbol, as a **signed** quantity: positive is long, negative is
  short, zero is flat. No single fill flips the sign — an opposing order reduces the position and
  clamps at zero, so reversing direction takes a second order from flat.
- **Round trip** — a complete entry-to-exit cycle (either direction: a long buys then sells, a short
  sells then covers), carrying realized P&L. The unit of per-trade analytics.
- **Execution model** — a pluggable rule the broker applies: commission, slippage, or sizing.
  (Order acceptance against Reg-T initial margin is enforced intrinsically by the account, not as a
  pluggable model — see below.)

---

## Writing a strategy

Derive from [`StrategyBase`](Backtester/Strategies/StrategyBase.cs) (which also gives you the opt-in
seam for exposing indicator series to the report). You get `OnStart` once with the full history, then
`OnBar` per bar to act through the broker.

```csharp
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;

/// <summary>Buys when price closes above the previous bar's high; flat otherwise.</summary>
public class BreakoutStrategy : StrategyBase
{
    // Key: symbol -> previous bar's high, used to detect a breakout on the next bar.
    private readonly Dictionary<string, decimal> _previousHigh = new();

    public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
    {
        bool hasPosition = snapshot.Positions.Any(p => p.Symbol == symbol && p.Quantity > 0);

        if (_previousHigh.TryGetValue(symbol, out decimal high) && bar.Close > high && !hasPosition)
        {
            broker.Submit(new OrderRequest
            {
                Symbol = symbol,
                Side   = OrderSide.Buy,
                Type   = OrderType.Market
            });
        }

        _previousHigh[symbol] = bar.High;
    }
}
```

The broker exposes four actions: `Submit`, `SubmitBracket`, `Cancel`, and `Modify`. For a
worked example of bracket orders and a trailing stop, see
[`AtrBracketStrategy`](Backtester/Strategies/AtrBracketStrategy.cs); for pre-computed signals from
history and long/short reversal (going short on a death cross, long on a golden cross), see
[`MovingAverageCrossStrategy`](Backtester/Strategies/MovingAverageCrossStrategy.cs).

### Pre-computing indicators

The engine ships no indicators. Compute yours in `OnStart` from the full history (with any library
you like), and optionally expose a named indicator so the report can draw it:

```csharp
public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
{
    foreach ((string symbol, IReadOnlyList<Candle> bars) in history)
    {
        List<IndicatorPoint> ema = ComputeEma(bars, period: 20);     // your own computation
        RecordIndicator("EMA(20)", symbol, IndicatorPane.PriceOverlay, ema);
    }
}
```

`RecordIndicator` wraps your line in a single-series `Indicator` placed on the pane you choose
(defaulting its shape by pane — a line on the price overlay, a filled area in a separate pane). For a
multi-series study, build the `Indicator` yourself and expose it with the `RecordIndicator(Indicator)`
overload — every series shares the indicator's pane and scale, each drawn by its own `IndicatorShape`:

```csharp
RecordIndicator(new Indicator("MACD", symbol, IndicatorPane.SeparatePane, new[]
{
    new IndicatorSeries("MACD", IndicatorShape.Line, macdLine),
    new IndicatorSeries("Signal", IndicatorShape.Line, signalLine),
    new IndicatorSeries("Histogram", IndicatorShape.Histogram, histogram)
}));
```

The report draws a separate-pane study the way a trader reads it: a histogram is coloured four ways by
sign and momentum (bright green / faded green above zero rising / falling, bright red / faded red below
zero falling / rising), and a faint zero line is drawn only in panes whose series cross zero (MACD, not
an always-positive ATR). These are render-time touches derived from the values — no colour or styling
is stored on the `Indicator` or its series.

Reading indicator values aligned to the current bar is lookahead-free because indicators are causal.

---

## Execution models

Mix and match — each is an interface with built-in implementations, and you can supply your own. All
are optional arguments to `BrokerSimulator`.

| Family | Interface | Built-ins |
|---|---|---|
| Commission | `ICommissionModel` | `FixedCommission`, `PercentCommission`, `PerShareCommission` |
| Slippage | `ISlippageModel` | `FixedSlippage`, `PercentSlippage` |
| Sizing | `ISizingModel` | `FixedSizeModel`, `PercentNotionalSizing`, `RiskPerTradeSizing` |

Order acceptance against **Reg-T initial margin** (50% long, 150% short) is not a pluggable model — it
is enforced intrinsically by the account, which rejects any opening order whose margin exceeds
`Portfolio.BuyingPower`.

**Risk-per-trade sizing** sizes a position so a stop-out loses a fixed fraction of realized equity.
Set both `Price` and `StopPrice` on the order so it can compute the stop distance:

```csharp
IBrokerSimulator broker = new BrokerSimulator(
    portfolio,
    sizingModel: new RiskPerTradeSizing { RiskFraction = 0.01m });  // risk 1% per trade
```

---

## Data sources

Data flows through two seams (see [`CONTEXT.md`](CONTEXT.md) for the distinction):

- **Provider** (`IHistoricalDataProvider`) — pure acquisition from an external service. Each live
  provider is an opt-in package, so the core stays network-free
  (see [ADR-0009](docs/adr/0009-network-providers-separate-packages.md)):
  - [`backtester.net.yahoo`](Backtester.Data.Yahoo/README.md) — Yahoo Finance v8 chart API
    (`1m`, `5m`, `15m`, `30m`, `1h`, `1d`, `1wk`, `1mo`, …); raw, unadjusted prices.
  - [`backtester.net.alpaca`](Backtester.Data.Alpaca/README.md) — Alpaca (US equities, consolidated
    SIP, split-adjusted by default).
- **Fetcher** (`IHistoricalDataFetcher`) — the cache-aware orchestrator the engine talks to. The
  fetchers and the offline CSV provider live in the core package.

Two fetchers ship in the box:

```csharp
// Online + cached: calls the provider only for bars the local CSV cache lacks.
// Add the backtester.net.yahoo package for the provider below (or backtester.net.alpaca).
IHistoricalDataFetcher live = new HistoricalDataFetcher(
    new YahooHistoricalDataProvider(),
    dataFolder: "data");   // defaults to ./data

// Fully offline + deterministic: reads committed CSV files, never touches the network.
// No provider package needed — CsvHistoricalDataFetcher is part of the core engine.
IHistoricalDataFetcher offline = new CsvHistoricalDataFetcher(dataFolder: "samples/data");
```

CSV files use the canonical name `SYMBOL_INTERVAL.csv` (e.g. `AAPL_1d.csv`) with a
`Timestamp,Open,High,Low,Close,Volume` header. Examples live in
[`samples/data`](samples/data) — the daily ETF files the
[analysis sample](samples/AnalysisSample/README.md) runs on. The offline fetcher makes backtests
reproducible — the same input
produces the same result every time, which is ideal for tests and CI.

### Priming for out-of-sample runs

The cache-aware fetcher tracks a **coverage floor** per symbol+interval — the earliest range start
ever asked of the provider. A run that starts *before* the floor is refused with a
`DataCoverageException` instead of being served a silently short slice, so an out-of-sample window you
never fetched fails loudly rather than producing a plausible curve from missing data. To run the same
instruments over several windows (in-sample, then out-of-sample) without repeated network trips, **prime**
the wide range once and let every sub-range read the warm cache:

```csharp
HistoricalDataFetcher fetcher = new(new YahooHistoricalDataProvider(), dataFolder: "data");

// Warm 2020→now once (no backtest runs here) …
await fetcher.PrimeAsync(new[] { "AAPL", "MSFT" }, new DateTime(2020, 1, 1), DateTime.UtcNow, "1d");

// … then in-sample and out-of-sample runs are served entirely from the cache.
```

See [ADR 0021](docs/adr/0021-coverage-floor-and-priming.md) for the design.

---

## Reporting

[`HtmlReportWriter`](Report/HtmlReportWriter.cs) renders a run into one self-contained HTML file
that opens from `file://` with no external dependencies — the chart library and the run data are
inlined.

```csharp
// Straight from a run:
new HtmlReportWriter().Write(result, "report.html");

// Build the HTML in memory instead of writing to disk:
string html = new HtmlReportWriter().BuildHtml(result);
```

The page renders a grouped stats panel (headline, trade-quality, run context), candlestick charts
with entry/exit markers and a symbol selector, your exposed indicator series, a portfolio equity
curve, and a sortable round-trips table.

Need just the data? [`ReportModelBuilder`](Report/ReportModelBuilder.cs) is a pure function from
`BacktestResult` to a serializable `ReportModel` — feed it to `System.Text.Json` (in-box on net8.0)
and render however you like.

```csharp
ReportModel model = new ReportModelBuilder().Build(result);
string json = System.Text.Json.JsonSerializer.Serialize(model);
```

### Surfacing your settings

Want the report to show *how the run was configured*? The opt-in
[`backtester.net.report.toolkit`](Report.Toolkit/README.md) package turns any settings object into
configuration cards without hand-writing settings tables. Decorate your parameter properties with
`ReportSettingAttribute` (a display label, a group, and an optional `Order`), and
`ConfigurationCardBuilder` reflects the object into one grouped card per group, which you pass to the
report writer's configuration overload:

```csharp
IReadOnlyList<ReportCard> cards = new ConfigurationCardBuilder().Build(parameters);
new HtmlReportWriter().Write(result, "report.html", cards);
```

A property you forget to attribute still shows up — in a catch-all `"Other"` card labelled by its
property name — so a changed parameter is never silently omitted. See the
[package README](Report.Toolkit/README.md) for grouping, ordering, opt-out, and formatting details.

---

## Analysis

An **Analysis** is a machine-generated critique of one run — a short summary plus a list of
**Findings**, each pairing an observation with the change it recommends — rendered as its own report
section. It is commentary, not measurement: it interprets the numbers the report already shows and
never produces one of its own.

Two opt-in packages, split along the network boundary:

- [`backtester.net.analysis`](Analysis/README.md) — the **Analyzer**, the **Analysis digest**, and the
  `IAnalysisClient` seam. It is AI-agnostic and **makes no outbound call, ever**.
- [`backtester.net.analysis.claude`](Analysis.Claude/README.md) — the **Analysis client** for Claude.
  All the network code lives here, so referencing the analysis package alone never pulls in a call.

Set `ANTHROPIC_API_KEY` — the feature's only credential — and the call site is this:

```csharp
// 1. Build the report model.
ReportModel model = new ReportModelBuilder().Build(result);

// 2. Attach the configuration cards BEFORE analysing.
model.Configuration = new ConfigurationCardBuilder().Build(settings);

// 3. Ask. The client carries the request; the Analyzer owns the contract.
IAnalysisClient client = new ClaudeAnalysisClient("claude-sonnet-5");
ReportAnalyzer analyzer = new ReportAnalyzer(client, new AnalysisOptions { ModelName = "claude-sonnet-5" });
model.Analysis = await analyzer.AnalyzeAsync(model, CancellationToken.None);

// 4. Write the report from the model.
new HtmlReportWriter().Write(model, "report.html");
```

The ordering is load-bearing, and the path is deliberately **model-first and explicit**:

- **Configuration must be attached before analysis.** The digest includes it, so a card attached
  afterwards reaches the reader but not the AI — and the Analyzer cannot comment on a setting it was
  never shown.
- **The one-call `Write(result, path, configuration)` convenience has no analysing counterpart, on
  purpose.** It builds the report model internally, where the caller cannot reach it, so there is
  nothing to hand the Analyzer — and extending it would turn writing a file into a network operation.
- **An Analysis can be regenerated from a saved report model.** The Analyzer consumes a `ReportModel`,
  which is serializable, so a stored model can be re-analysed — with different guidance or a different
  model — without re-running the backtest.

Other things worth knowing before you wire it up:

- **The minimum viable model is Sonnet-class.** Six models were evaluated by hand against a real run's
  digest first; below that floor they misattribute real digest figures to the wrong symbol or concept.
  There is no locally hosted option.
- **The digest is bounded by round-trip count, not tokens.** A run with more round trips than
  `RoundTripCap` (500 by default) is rejected with an `AnalysisDigestOverflowException`; sampling is
  opt-in via `OverflowPolicy`, and a sampled digest declares its own sampling inside the payload.
- **A malformed Analysis is rejected, never repaired.** A contract violation costs exactly one retry
  carrying the validation error back; a second throws `AnalysisFormatException`. Catch it and write the
  report anyway — `model.Analysis` stays null, no Analysis section renders, and a report written
  without one is **exactly the report you get today**.

A complete, runnable example lives in [`samples/AnalysisSample`](samples/AnalysisSample/README.md). It
reads committed CSV bars, so the Claude call is its only outbound call. It is not part of the test
suite: no test depends on an API key or on network access.

---

## Optimization

**Optimization** sweeps a strategy's **Parameters** over a grid, runs a full backtest per combination
(a **Trial**), and ranks the Trials by an **Objective** — so you can ask "sweep the fast period from 5
to 15 and the slow period from 20 to 40, run them all, and show me which did best and how the neighbours
compare." It ships in the opt-in [`backtester.net.optimization`](Optimization/README.md) package and
makes no outbound call.

The documented lead is **attributes-first**: mark the Parameters to vary with `[Optimize(min, max, step)]`
(a `bool` uses the parameterless `[Optimize]` and expands to `{ false, true }`), and supply one factory
that builds a Trial's strategy *and* broker from a Parameter set — so a Parameter driving an execution
model (e.g. `RiskFraction` → risk-per-trade sizing) actually takes effect instead of being a silent no-op.

```csharp
using Backtester.Optimization;

public class MaCrossParameters
{
    [Optimize(5, 15, 5)]    // 5, 10, 15
    public int FastPeriod { get; set; }

    [Optimize(20, 40, 10)]  // 20, 30, 40
    public int SlowPeriod { get; set; }
}

// Attributes-first authoring: the [Optimize] properties become the swept axes, and the factory
// realizes each Parameter set into a fresh strategy and broker.
OptimizationSetup setup = Optimize
    .For(new MaCrossParameters(), (parameters, portfolio) =>
        (new MovingAverageCrossStrategy(parameters.FastPeriod, parameters.SlowPeriod),
         new BrokerSimulator(portfolio)))
    .FromAttributes();

Optimizer optimizer = new Optimizer(
    fetcher,
    symbols: new[] { "SPY", "QQQ" },
    fromUtc, toUtc, interval: "1d",
    portfolioFactory: () => new Portfolio(100_000m),
    setup,
    objective: Objectives.Sharpe);   // the default; or Calmar, NetProfit, MinDrawdown, …

OptimizationResult result = await optimizer.RunAsync();

// The whole sweep as a leaderboard-and-heatmap report:
new OptimizationHtmlReportWriter().Write(
    new OptimizationReportModelBuilder().Build(result), "optimization.html");

// The winner's full BacktestResult is exposed, so you render its single-run report yourself:
new HtmlReportWriter().Write(result.Best.BacktestResult, "winner.html");
```

Two more authoring paths compile to the same primitive: a fluent
`.Vary(parameters => parameters.FastPeriod, 5, 15, 5)` that keeps the search space in the experiment
rather than on the strategy, and an explicit `ParameterSpace` for a bare-constructor strategy with no
parameters class.

The Optimizer fetches the bars **once** and shares them across every Trial, runs Trials in **parallel**
with a progress callback and `CancellationToken`, and reuses the existing `Engine` unchanged. The
Optimization report renders the whole sweep as a sortable leaderboard — best highlighted, ineligible
Trials flagged — with a Score **heatmap** when exactly two Parameters vary (per-Parameter marginals
otherwise).

**A winning Trial is the best *in-sample* configuration, not a validated one.** v1 is in-sample grid
search only ([ADR 0020](docs/adr/0020-optimization-in-sample-grid-search.md)): it tunes and reports on
the same data, with no out-of-sample or walk-forward split. Overfitting is guarded only by a
minimum-trades **Eligibility** rule (a Trial with too few round trips cannot win) and by surfacing the
full ranked leaderboard.

A complete, offline example lives in
[`samples/OptimizationSample`](samples/OptimizationSample/README.md); the
[package README](Optimization/README.md) covers the authoring paths, Objectives, and Eligibility in full.

---

## Project layout

```
Backtester/            The engine (backtester.net)
  Core/                Candle, Order, Trade, Position, Portfolio, PerformanceStats, MarketSlice, …
  Engine/              Engine, IEngine, BacktestResult
  Broker/              BrokerSimulator, IFillModel, FillModel_OHLCHeuristic
  Data/                Data seams, HistoricalDataFetcher, CSV provider/fetcher, CsvBarLoader
  Strategies/          IStrategy, StrategyBase, reference strategies
  ExecutionModels/     Commission, Slippage, Sizing, Risk models
Backtester.Data.Yahoo/   Yahoo Finance provider (backtester.net.yahoo)
Backtester.Data.Alpaca/  Alpaca provider (backtester.net.alpaca)
Report/                HTML reporting (backtester.net.report)
Report.Toolkit/        Settings-to-cards helper (backtester.net.report.toolkit)
Analysis/              AI-agnostic Analysis (backtester.net.analysis)
Analysis.Claude/       Claude Analysis client (backtester.net.analysis.claude)
Optimization/          Parameter Optimization (backtester.net.optimization)
BacktesterTests/       Test suite
samples/data/          Example OHLCV CSVs
samples/AnalysisSample/      End-to-end run ending in a report with an Analysis
samples/OptimizationSample/  End-to-end offline Parameter sweep writing both reports
CONTEXT.md             The engine's ubiquitous language (glossary)
```

Each library has its own focused README: [`Backtester/README.md`](Backtester/README.md),
[`Backtester.Data.Yahoo/README.md`](Backtester.Data.Yahoo/README.md),
[`Backtester.Data.Alpaca/README.md`](Backtester.Data.Alpaca/README.md),
[`Report/README.md`](Report/README.md),
[`Report.Toolkit/README.md`](Report.Toolkit/README.md),
[`Analysis/README.md`](Analysis/README.md),
[`Analysis.Claude/README.md`](Analysis.Claude/README.md), and
[`Optimization/README.md`](Optimization/README.md).

---

## Requirements

- .NET 8 SDK or later. The libraries target `net8.0`.

<!-- ## Contributing

Issues and pull requests are welcome. The codebase follows the conventions in
[`CLAUDE.md`](CLAUDE.md) (one type per file, explicit types, DDD-flavoured layering) and a shared
vocabulary documented in [`CONTEXT.md`](CONTEXT.md) — please keep new code consistent with both. -->
