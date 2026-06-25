# backtester.net

A bar-by-bar backtesting engine for financial market strategies, written in C# on .NET 8.

## Features

- **Bar-by-bar simulation** — the engine fetches historical candles, synchronizes them, and steps through them one bar at a time, matching the rhythm of a live trading loop.
- **Strategy interface** — implement `IStrategy.OnStart(history)` to pre-compute indicators from the full bar history (using any library), then `IStrategy.OnBar(symbol, bar, snapshot, broker)` to submit orders directly via the broker.
- **Broker simulation** — `BrokerSimulator` fills orders using an OHLC heuristic, supports market, limit, and stop order types, bracket orders with OCO exit legs, and tracks open positions through `Portfolio`.
- **Long and short** — positions carry a signed quantity (long, short, or flat). A sell from flat opens a short, a buy covers it, and short brackets arm opposite-side protective legs. No single fill flips a position's sign, so reversing direction flattens first, then opens the opposite side.
- **Pluggable models** — swap in your own implementations of `IFillModel`, `ICommissionModel`, `ISlippageModel`, and `ISizingModel` without touching engine code.
- **Reg-T margin account** — the account enforces initial margin intrinsically (50% long, 150% short), rejecting any opening order whose margin exceeds `Portfolio.BuyingPower` (marked equity less the margin already committed).
- **Data seams** — the engine fetches each symbol through `IHistoricalDataFetcher` and synchronizes multi-symbol data internally. The core ships the cache-aware `HistoricalDataFetcher`, the offline `CsvHistoricalDataFetcher`, and `CsvBarLoader`; live network providers are opt-in packages (`backtester.net.yahoo`, `backtester.net.alpaca`).
- **Performance stats** — `Portfolio.GetPerformanceStats()` returns win rate, profit factor, expectancy, max drawdown, CAGR, Sharpe, and more, computed from completed round trips.

## Quick start

```csharp
// 1. Implement a strategy
IStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 10, slowPeriod: 50);

// 2. Wire up the broker and portfolio
//    Risk sizing uses realized equity (cash + cost basis of open positions, excluding unrealized PnL)
Portfolio portfolio = new Portfolio(initialCash: 100_000m);
IBrokerSimulator broker = new BrokerSimulator(
    portfolio,
    commissionModel: new FixedCommission { Amount = 1m },
    slippageModel: new FixedSlippage { Amount = 0.05m },
    sizingModel: new FixedSizeModel { FixedSize = 10 });

// 3. Create a data fetcher. The offline CSV fetcher ships in this package and needs no
//    network; for live data add a provider package (backtester.net.yahoo or
//    backtester.net.alpaca) and pass its provider to HistoricalDataFetcher instead.
IHistoricalDataFetcher fetcher = new CsvHistoricalDataFetcher(dataFolder: "data");

// 4. Run — the engine fetches the data, synchronizes it, and steps through it bar by bar
IEngine engine = new Engine(
    fetcher,
    symbols: new[] { "AAPL" },
    fromUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    toUtc:   new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    interval: "1d",
    strategy,
    broker,
    portfolio);
// StartAsync returns a BacktestResult bundling the run's candle history, portfolio,
// and (once exposed) indicator series — a single source of truth for reporting.
BacktestResult result = await engine.StartAsync();

PerformanceStats stats = result.Portfolio.GetPerformanceStats();
```

## Namespaces

| Namespace | Contents |
|---|---|
| `Backtester.Core` | `Candle`, `Order`, `Trade`, `Position`, `Portfolio`, `PortfolioSnapshot`, `PerformanceStats`, `MarketSlice`, `Indicator`, `IndicatorSeries`, `IndicatorShape`, `IndicatorPoint`, `IndicatorPane` |
| `Backtester.Engine` | `Engine`, `IEngine`, `BacktestResult` |
| `Backtester.Broker` | `BrokerSimulator`, `IFillModel`, `FillModel_OHLCHeuristic` |
| `Backtester.Data` | `IHistoricalDataProvider`, `IHistoricalDataFetcher`, `HistoricalDataFetcher`, `CsvHistoricalDataFetcher`, `CsvBarLoader` |
| `Backtester.Strategies` | `IStrategy`, `IIndicatorSource`, `StrategyBase`, `MovingAverageCrossStrategy`, `AtrBracketStrategy` |
| `Backtester.ExecutionModels.*` | Commission, slippage, sizing, and risk model interfaces and built-in implementations |

## Requirements

- .NET 8 or later
