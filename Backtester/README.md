# backtester.net

A bar-by-bar backtesting engine for financial market strategies, written in C# on .NET 8.

## Features

- **Bar-by-bar simulation** — the engine fetches historical candles, synchronizes them, and steps through them one bar at a time, matching the rhythm of a live trading loop.
- **Strategy interface** — implement `IStrategy.OnStart(history)` to pre-compute indicators from the full bar history (using any library), then `IStrategy.OnBar(symbol, bar, snapshot, broker)` to submit orders directly via the broker.
- **Broker simulation** — `BrokerSimulator` fills orders using a gap-aware OHLC heuristic (fills are bounded by the bar open), supports market, limit, and stop order types, bracket orders with one or two protective legs (a stop-loss and/or take-profit, each as an absolute price or a fill-relative offset resolved against the actual fill; two legs form an OCO group), and tracks open positions through `Portfolio`. A protective leg already through the market at its arming bar's open fills on that same bar, as a live bracket would. `SubmitBracket` returns a `BracketHandle` reporting the bracket's `BracketState` — `Pending`, `Armed`, `Retired` — alongside its leg order ids, so a strategy asks the handle where its bracket stands rather than testing whether a leg id happens to be set (a target-only bracket never gets a stop order id). Several brackets may be live on one symbol; each answers only for its own legs, and a signal exit that flattens the position cancels the resting legs of all of them.
- **Long and short** — positions carry a signed quantity (long, short, or flat). A sell from flat opens a short, a buy covers it, and short brackets arm opposite-side protective legs. No single fill flips a position's sign, so reversing direction flattens first, then opens the opposite side. When both orders fill on the same bar, set `OrderRequest.Priority` (higher fills sooner) on the flatten so it is applied before the reversing entry, letting the entry open from flat and carry its protective stop.
- **Pluggable models** — swap in your own implementations of `IFillModel`, `ICommissionModel`, `ISlippageModel`, and `ISizingModel` without touching engine code. The broker never writes to a request a strategy hands it: the sized `Quantity` and a bracket's `StopOffset` land on the broker's own copy, so an `OrderRequest` held and resubmitted across bars keeps the shape it was built with.
- **Reg-T margin account** — the account enforces initial margin intrinsically (50% long, 150% short), rejecting any opening order whose margin exceeds `Portfolio.BuyingPower` (marked equity less the margin already committed). An `Instrument`'s `MarginRate`, when set, overrides this Reg-T split with a single symmetric rate for both long and short on that symbol (e.g. 2% for 50:1 forex leverage) — see [ADR 0030](../docs/adr/0030-forex-margin-via-per-instrument-leverage.md).
- **Multi-currency accounting** — `Portfolio(startingCash, accountCurrency, instruments)` denominates cash and equity in one `AccountCurrency` (defaulting to `"USD"`, so every existing call keeps working unchanged). An `Instrument` whose `QuoteCurrency` differs from the account's declares a `ConversionSymbol` and, for a quote-first pair such as `GBP_USD`, `ConversionOperation.Multiply` (`Divide` is the default); `Engine` fetches that series alongside tradable symbols and `Portfolio` converts `Cash`, `RealizedPnL`, `MarkedEquity`, `RealizedEquity` and isolated equity through its latest rate. The declarations are cross-checked when the `Portfolio` is built (quote currency required; differing from the account's requires a `ConversionSymbol`, equal to it forbids one) and applying a declared conversion with no observed rate throws rather than silently returning the native amount. `Position.AveragePrice` and `RoundTrip.EntryPrice`/`ExitPrice` stay in the instrument's native quote currency, and `RiskPerTradeSizing`/`FixedRiskSizing` convert the stop distance the same way before sizing — see [ADR 0029](../docs/adr/0029-instrument-and-multi-currency-forex-accounting.md). `RiskPerTradeSizing` budgets against `Portfolio.RealizedEquity`, the same translated figure `SnapshotAt` reports as `CostBasisEquity`, so an open cross-currency position cannot inflate the equity the next trade is sized against — see [ADR 0032](../docs/adr/0032-round-trips-carry-account-currency-figures.md).
- **Round trips carry account-currency figures** — the `Portfolio` stamps each `RoundTrip` as it closes: `InitialRisk`, `EntryNotional`, `EntryMargin` and `RealizedPnL` in the `AccountCurrency`, each translated at the rate in force at its own moment (risk, notional and margin at entry; P&L at exit), alongside the native `EntryPrice`/`ExitPrice`/`EntryStopPrice`/`EntryTargetPrice` and the `QuoteCurrency` those four are in. A consumer divides like units instead of re-translating a historical entry: a trip that risked 100 JPY at `USD_JPY` 100 and made 200 JPY at 125 carries risk `1.00`, profit `1.60` and so reports `1.6R`, not the `2.0R` a single rate produces — see [ADR 0032](../docs/adr/0032-round-trips-carry-account-currency-figures.md).
- **Currency converter** — the conversion rule and its rate state live in one module, `CurrencyConverter`, built and owned by the `Portfolio`. The `Portfolio` is the **single hand-off point for Instruments**: no `Engine` constructor takes an `Instrument[]`, and the `Engine` derives the conversion series to fetch from the Portfolio's declarations, so the two cannot disagree about which symbols convert through what. Its timing rule is an invariant of the module: a fill translates at the conversion pair's previous close (never a rate unknowable while that bar traded), an end-of-bar mark at its current close — see [ADR 0031](../docs/adr/0031-currency-converter-module.md).
- **Data seams** — the engine fetches each symbol through `IHistoricalDataFetcher` and synchronizes multi-symbol data internally. The core ships the cache-aware `HistoricalDataFetcher`, the offline `CsvHistoricalDataFetcher`, and `CsvBarLoader`; live network providers are opt-in packages (`backtester.net.data.yahoo`, `backtester.net.data.alpaca`, `backtester.net.data.oanda`).
- **Coverage floor & priming** — the cache-aware fetcher records the earliest range start ever asked of the provider (a per-symbol+interval sidecar) and refuses a run that starts before it with a `DataCoverageException`, rather than silently serving a short slice. `IDataPrimer.PrimeAsync` warms a wide range up front so in-sample and out-of-sample sub-ranges run entirely from the cache.
- **Performance stats** — `Portfolio.GetPerformanceStats()` returns win rate, profit factor, expectancy, max drawdown, CAGR, Sharpe, and more, computed from completed round trips.

## Quick start

```csharp
// 1. Implement a strategy
IStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 10, slowPeriod: 50);

// 2. Wire up the broker and portfolio
//    Risk sizing uses realized equity (cash + cost basis of open positions, excluding unrealized PnL)
Portfolio portfolio = new Portfolio(initialCash: 100_000m);
BrokerSimulator broker = new BrokerSimulator(
    portfolio,
    commissionModel: new FixedCommission { Amount = 1m },
    slippageModel: new FixedSlippage { Amount = 0.05m },
    sizingModel: new FixedSizeModel { FixedSize = 10 });

// 3. Create a data fetcher. The offline CSV fetcher ships in this package and needs no
//    network; for live data add a provider package (backtester.net.data.yahoo or
//    backtester.net.data.alpaca) and pass its provider to HistoricalDataFetcher instead.
IHistoricalDataFetcher fetcher = new CsvHistoricalDataFetcher(dataFolder: "data");

// 4. Run — the engine fetches the data, synchronizes it, and steps through it bar by bar
IEngine engine = new Engine(
    fetcher,
    symbols: new[] { "AAPL" },
    testFrom: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    testTo:   new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    interval: "1d",
    strategy,
    broker,
    portfolio);
// testFrom/testTo are the Test range: the window looped, measured, and reported. To warm a lookback
// indicator, add a Warmup lead-in after testTo — a TimeSpan, an absolute DateTime start, or a bar-count
// int (e.g. `warmupBars: 200`). Warmup bars reach OnStart's history only; they are never looped, so the
// run's results stay confined to the Test range.
// StartAsync returns a BacktestResult bundling the run's candle history, portfolio,
// and (once exposed) indicator series — a single source of truth for reporting.
BacktestResult result = await engine.StartAsync();

PerformanceStats stats = result.Portfolio.GetPerformanceStats();
```

## Namespaces

| Namespace | Contents |
|---|---|
| `Backtester.Core` | `Candle`, `Order`, `Trade`, `Position`, `Portfolio`, `Instrument`, `ConversionOperation`, `CurrencyConverter`, `PortfolioSnapshot`, `PerformanceStats`, `MarketSlice`, `BracketRequest`, `BracketLegSpec`, `BracketHandle`, `BracketState`, `BracketLeg`, `Indicator`, `IndicatorSeries`, `IndicatorShape`, `IndicatorPoint`, `IndicatorPane` |
| `Backtester.Engine` | `Engine`, `IEngine`, `BacktestResult` |
| `Backtester.Broker` | `IBroker`, `BrokerSimulator`, `IFillModel`, `FillModel_OHLCHeuristic` |
| `Backtester.Data` | `IHistoricalDataProvider`, `IHistoricalDataFetcher`, `HistoricalDataFetcher`, `CsvHistoricalDataFetcher`, `CsvBarLoader`, `IDataPrimer`, `CoverageFloorLoader`, `DataCoverageException` |
| `Backtester.Strategies` | `IStrategy`, `IIndicatorSource`, `StrategyBase`, `MovingAverageCrossStrategy`, `AtrBracketStrategy` |
| `Backtester.ExecutionModels.*` | Commission, slippage, sizing, and risk model interfaces and built-in implementations |

## Requirements

- .NET 8 or later
