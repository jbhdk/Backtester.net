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

The repository builds four NuGet packages. Use the engine on its own, and add a data source or
reporting when you want them. The core makes no outbound network call on its own — every live data
provider is an opt-in package (see [ADR-0009](docs/adr/0009-network-providers-separate-packages.md)).

| Package | What it is | Depends on |
|---|---|---|
| [`backtester.net`](Backtester/README.md) | The backtesting engine: the data seams, the cache-aware fetcher, the offline CSV provider, broker, portfolio, strategies, execution models. | — |
| [`backtester.net.yahoo`](Backtester.Data.Yahoo/README.md) | Opt-in Yahoo Finance data provider. BCL-only. | `backtester.net` |
| [`backtester.net.alpaca`](Backtester.Data.Alpaca/README.md) | Opt-in Alpaca data provider (US equities, consolidated SIP, split-adjusted). | `backtester.net`, `Alpaca.Markets` |
| [`backtester.net.report`](Report/README.md) | Opt-in HTML reporting built from a run's `BacktestResult`. Kept separate so the engine takes on no web-asset dependencies. | `backtester.net` |

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
- **Position** — the net holding in a symbol. This engine is **long-only**: a Sell may only reduce
  or close a long.
- **Round trip** — a complete entry-to-exit cycle, carrying realized P&L. The unit of per-trade
  analytics.
- **Execution model** — a pluggable rule the broker applies: commission, slippage, sizing, or risk.

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
history, see [`MovingAverageCrossStrategy`](Backtester/Strategies/MovingAverageCrossStrategy.cs).

### Pre-computing indicators

The engine ships no indicators. Compute yours in `OnStart` from the full history (with any library
you like), and optionally expose a named series so the report can draw it:

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
`Timestamp,Open,High,Low,Close,Volume` header. A small example lives in
[`samples/data`](samples/data). The offline fetcher makes backtests reproducible — the same input
produces the same result every time, which is ideal for tests and CI.

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
BacktesterTests/       Test suite
samples/data/          Example OHLCV CSV
CONTEXT.md             The engine's ubiquitous language (glossary)
```

Each library has its own focused README: [`Backtester/README.md`](Backtester/README.md),
[`Backtester.Data.Yahoo/README.md`](Backtester.Data.Yahoo/README.md),
[`Backtester.Data.Alpaca/README.md`](Backtester.Data.Alpaca/README.md), and
[`Report/README.md`](Report/README.md).

---

## Requirements

- .NET 8 SDK or later. The libraries target `net8.0`.

<!-- ## Contributing

Issues and pull requests are welcome. The codebase follows the conventions in
[`CLAUDE.md`](CLAUDE.md) (one type per file, explicit types, DDD-flavoured layering) and a shared
vocabulary documented in [`CONTEXT.md`](CONTEXT.md) — please keep new code consistent with both. -->
