# backtester.net

A bar-by-bar backtesting engine for financial market strategies, written in C# on .NET 8.

## Features

- **Bar-by-bar simulation** — the engine steps through historical candles one bar at a time via `IMarketDataFeed`, matching the rhythm of a live trading loop.
- **Strategy interface** — implement `IStrategy.OnBar(symbol, bar, snapshot)` to emit `OrderRequest` objects; the engine handles the rest.
- **Broker simulation** — `BrokerSimulator` fills orders using an OHLC heuristic, supports market and limit order types, and tracks open positions through `Portfolio`.
- **Pluggable models** — swap in your own implementations of `IFillModel`, `ICommissionModel`, `ISlippageModel`, `ISizingModel`, and `IRiskModel` without touching engine code.
- **Data providers** — load bars from CSV files (`CsvBarLoader`) or fetch historical data from Yahoo Finance (`YahooHistoricalDataProvider`); multi-symbol feeds are synchronized by `MarketDataSynchronizer`.
- **Performance stats** — `PerformanceStats` and equity snapshots are recorded at every bar for post-run analysis.

## Quick start

```csharp
// 1. Build a data feed
IMarketDataFeed feed = new MarketDataSynchronizer(new Dictionary<string, IHistoricalDataProvider>
{
    ["AAPL"] = new YahooHistoricalDataProvider("AAPL", from, to)
});

// 2. Implement a strategy
IStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 10, slowPeriod: 50);

// 3. Wire up the broker and portfolio
Portfolio portfolio = new Portfolio(initialCash: 100_000m);
IBrokerSimulator broker = new BrokerSimulator(portfolio, new FillModel_OHLCHeuristic(),
    new PercentCommission(0.001m), new PercentSlippage(0.0005m));

// 4. Run
IEngine engine = new Engine(feed, strategy, broker, portfolio);
engine.Start();

PerformanceStats stats = portfolio.GetPerformanceStats();
```

## Namespaces

| Namespace | Contents |
|---|---|
| `Backtester.Core` | `Candle`, `Order`, `Trade`, `Position`, `Portfolio`, `PortfolioSnapshot`, `PerformanceStats` |
| `Backtester.Engine` | `Engine`, `EngineRunner`, `IEngine` |
| `Backtester.Broker` | `BrokerSimulator`, `OrderManager`, `IFillModel`, `FillModel_OHLCHeuristic` |
| `Backtester.Data` | `CsvBarLoader`, `YahooHistoricalDataProvider`, `MarketDataSynchronizer` |
| `Backtester.Strategies` | `IStrategy`, `StrategyBase`, `MovingAverageCrossStrategy` |
| `Backtester.Models.*` | Commission, slippage, sizing, and risk model interfaces and built-in implementations |

## Requirements

- .NET 8 or later
