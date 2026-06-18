# backtester.net

A bar-by-bar backtesting engine for financial market strategies, written in C# on .NET 8.

## Features

- **Bar-by-bar simulation** — the engine steps through historical candles one bar at a time via `IMarketDataFeed`, matching the rhythm of a live trading loop.
- **Strategy interface** — implement `IStrategy.OnStart(history)` to pre-compute indicators from the full bar history (using any library), then `IStrategy.OnBar(symbol, bar, snapshot, broker)` to submit orders directly via the broker.
- **Broker simulation** — `BrokerSimulator` fills orders using an OHLC heuristic, supports market, limit, and stop order types, bracket orders with OCO exit legs, and tracks open positions through `Portfolio`.
- **Pluggable models** — swap in your own implementations of `IFillModel`, `ICommissionModel`, `ISlippageModel`, `ISizingModel`, and `IRiskModel` without touching engine code.
- **Data providers** — load bars from CSV files (`CsvBarLoader`) or fetch historical data from Yahoo Finance (`YahooHistoricalDataProvider`); multi-symbol feeds are synchronized by `MarketDataSynchronizer`.
- **Performance stats** — `Portfolio.GetPerformanceStats()` returns win rate, profit factor, expectancy, max drawdown, CAGR, Sharpe, and more, computed from completed round trips.

## Quick start

```csharp
// 1. Build a data feed
IMarketDataFeed feed = await MarketDataSynchronizer.CreateFromProvidersAsync(
    new Dictionary<string, IHistoricalDataProvider>
    {
        ["AAPL"] = new YahooHistoricalDataProvider()
    },
    fromUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    toUtc:   new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    interval: "1d");

// 2. Implement a strategy
IStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 10, slowPeriod: 50);

// 3. Wire up the broker and portfolio
//    Risk sizing uses realized equity (cash + cost basis of open positions, excluding unrealized PnL)
Portfolio portfolio = new Portfolio(initialCash: 100_000m);
IBrokerSimulator broker = new BrokerSimulator(
    portfolio,
    commissionModel: new FixedCommission { Amount = 1m },
    slippageModel: new FixedSlippage { Amount = 0.05m },
    sizingModel: new FixedSizeModel { FixedSize = 10 });

// 4. Run
IEngine engine = new Engine(feed, strategy, broker, portfolio);
engine.Start();

PerformanceStats stats = portfolio.GetPerformanceStats();
```

## Namespaces

| Namespace | Contents |
|---|---|
| `Backtester.Core` | `Candle`, `Order`, `Trade`, `Position`, `Portfolio`, `PortfolioSnapshot`, `PerformanceStats` |
| `Backtester.Engine` | `Engine`, `IEngine` |
| `Backtester.Broker` | `BrokerSimulator`, `IFillModel`, `FillModel_OHLCHeuristic` |
| `Backtester.Data` | `CsvBarLoader`, `YahooHistoricalDataProvider`, `MarketDataSynchronizer` |
| `Backtester.Strategies` | `IStrategy`, `StrategyBase`, `MovingAverageCrossStrategy`, `AtrBracketStrategy` |
| `Backtester.ExecutionModels.*` | Commission, slippage, sizing, and risk model interfaces and built-in implementations |

## Requirements

- .NET 8 or later
