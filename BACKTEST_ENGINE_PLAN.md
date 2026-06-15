# Backtesting Engine — Implementation Plan (C# / .NET 8)

## Purpose & Scope

- Build a bar-by-bar (event-driven) historical backtesting engine in C# targeting .NET 10.
- Focus: correctness, clarity, testability, and extensibility for multi-symbol systematic strategies.
- Deliverables: engine core, broker simulator, portfolio accounting, strategy API (signals/orders only), commission/slippage/sizing models, equity & trade history, HTML visualization, and unit tests.
- Non-goals (initial): live trading adapters, tick-level/backfill microstructure, exchange connectivity.

## High-level goals & constraints

- Event-driven, bar-by-bar processing (OHLCV). Support hourly timeframes initially, but prepared for more timeframes in the future.
- Multiple symbols simultaneously, portfolio-level capital management, long & short, and pyramiding.
- Market, Stop orders; dynamic stop / take-profit updates.
- Capital competition between signals on the same bar: portfolio decides allocation.
- Use interfaces so alternative feed/broker/models can be swapped in for testing.

## Design Principles

- Single source of truth: `Portfolio` owns cash, positions, and risk exposure.
- Separation of concerns: `Strategy` => emits `Signal` / `OrderRequest`; `BrokerSimulator` => executes orders; `Engine` => orchestrates event loop; `MarketData` => supplies bars.
- Testability: pure logic in small, interface-based components; deterministic fill models.
- Extensible: clear interfaces for commission, slippage, sizing, fill logic and data feeds.

## Proposed project layout

- Backtester/
  - Core/  (domain model and core interfaces; one file per type)
    - Candle.cs                 (class `Candle` / `Bar`)
    - Order.cs                  (class `Order`)
    - OrderRequest.cs
    - OrderType.cs              (enums)
    - Trade.cs                  (class `Trade`)
    - Position.cs               (class `Position`)
    - PositionMetadata.cs       (typed metadata/state container)
    - Portfolio.cs              (class `Portfolio`)
    - PortfolioSnapshot.cs
    - EquitySnapshot.cs
    - PerformanceStats.cs
  - Engine/  (engine orchestration; one file per class)
    - IEngine.cs
    - IMarketDataFeed.cs
    - Engine.cs
    - EngineRunner.cs
    - MarketDataSynchronizer.cs
    - MarketSlice.cs
    - CsvBarLoader.cs
  - Broker/  (broker simulator and execution helpers)
    - IFillModel.cs
    - IBrokerSimulator.cs
    - BrokerSimulator.cs
    - OrderManager.cs
    - FillModel_OHLCHeuristic.cs
    - FillResult.cs
  - Models/  (pluggable models grouped into folders; one file per model)
    - Commission/
      - ICommissionModel.cs
      - FixedCommission.cs
      - PerShareCommission.cs
      - PercentCommission.cs
    - Slippage/
      - ISlippageModel.cs
      - FixedSlippage.cs
      - PercentSlippage.cs
    - Sizing/
      - ISizingModel.cs
      - FixedSizeModel.cs
      - RiskPercentSizing.cs
    - Risk/
      - IRiskModel.cs
      - PortfolioRiskModel.cs
  - Strategies/  (sample strategies; each strategy in its own file)
    - IStrategy.cs
    - StrategyBase.cs
    - MovingAverageCrossStrategy.cs
    - ExampleStrategyState.cs
- BacktesterTests/
  - Core.Tests/
    - PortfolioTests.cs
    - PositionTests.cs
    - OrderExecutionTests.cs
  - Engine.Tests/
    - EngineTests.cs
  - Broker.Tests/
    - BrokerSimulatorTests.cs
- samples/data/            (small OHLCV CSVs for tests/integration)
- viz/                     (HTML + JSON exporter for charts; one JSON per export artifact)
- BACKTEST_ENGINE_PLAN.md  (this file)
- README.md

Conventions:

- One file, one primary type: every public class, struct, enum, or interface gets its own .cs file named to match the declared type.
- Namespaces mirror folder/project names (e.g. `Backtester.Core`, `Backtester.Engine`).
- Tests follow the same rule: one test class per file; test file names mirror the production type (`PortfolioTests.cs` tests `Portfolio`).
- Keep files small and focused to make reviews and unit testing straightforward.
- Keep interfaces in the same folder as the implementations. Do not make explicit Interfaces folders.

## Core components and responsibilities

- `MarketData`
  - Supplies time-aligned OHLCV bars for multiple symbols.
  - Exposes an indexable, read-only history per symbol and a synchronized time cursor.

- `Engine`
  - Implements the main event loop (see event loop section).
  - Calls strategies, gathers order requests, routes to `BrokerSimulator` and `Portfolio`.

- `Strategy` (interface)
  - Pure: reads market data and portfolio snapshot, returns `Signal`/`OrderRequest` objects.
  - Must not mutate portfolio state directly.

- `BrokerSimulator`
  - Processes order submission and determines fills using a pluggable `IFillModel` (uses OHLCV heuristics).
  - Applies `ISlippageModel` and `ICommissionModel` and creates `Trade` records.

- `Portfolio`
  - Single source of truth for cash, positions, reserved cash, realized/unrealized P&L, and risk exposure.
  - Implements capital allocation logic and heat/risk checks for competing signals.

- `Position`
  - Aggregates one or more `Trade` legs (support pyramiding).
  - Exposes strategy-visible `Metadata` / `State` (e.g., `PyramidCount`, `HighestCloseSinceEntry`, `BreakoutDate`, `TrendStrength`).

- `Order`, `Trade`
  - `Order` is an intention (Market/Limit/Stop, side, size, optional price, client metadata, priority).
  - `Trade` is the execution record (entry or exit), with filled price, quantity, commissions, slippage, timestamps, and parent position id.

## Key interfaces (conceptual)

- `IMarketDataFeed` — time cursor, get-bars(symbol), lookback, subscribe.
- `IEngine` — Start/Stop/RunOnce (advance one bar).
- `IStrategy` — OnBar(symbol, bar, marketData, portfolioSnapshot) => IEnumerable<OrderRequest>
- `IBrokerSimulator` — SubmitOrder(OrderRequest) => OrderId; ProcessBar(barTime) => fills
- `IFillModel` — Determine fills given existing orders and next bar OHLCV.
- `ISlippageModel`, `ICommissionModel` — compute adjustments/fees per fill.
- `ISizingModel` — convert signals to order quantities respecting risk rules.
- `IRiskModel` — checks portfolio heat, per-symbol caps, margin rules.

## Event loop (detailed)

Each iteration processes one synchronized bar timestamp across all symbols.

1. Advance to next bar timestamp in `MarketData`.
2. Update all in-memory market series; update indicators and cached values.
3. Expose portfolio snapshot (cash/equity/positions/unrealized P&L) for this timestamp.
4. Process pending orders queued for this timestamp (prioritize OCO/stop/limit cancels).
5. Update stops & take-profit orders (strategy can request updates to its `Position` via OrderRequests).
6. Call each `IStrategy.OnBar(...)` with the updated snapshot — collect `OrderRequest`s and `Signal`s.
7. Aggregate all new entry requests and run capital-allocation / competition algorithm (see below).
8. Submit approved orders to `BrokerSimulator` which uses `IFillModel` and current bar OHLCV to produce fills.
9. Apply fills: create `Trade` records, update `Position` (legs/pyramid), adjust `Portfolio` cash/reserved balances and realized P&L.
10. Persist equity snapshot and trade history for this timestamp (used later for performance calculations and viz).

Note: ordering matters — strategies see the same market state before fills for that bar, and fills apply after allocation and matching.

## Order types & fill semantics (bar-by-bar, no intra-tick detail)

- Market orders: filled at next bar open price (default), with slippage applied by `ISlippageModel`.
- Stop orders: considered triggered when bar High/Low crosses the stop price; fill at stop price (or next bar open if configured) ± slippage.

Heuristics & tie-breakers
- Because we only have OHLC, we must assume a price path to disambiguate (Open->High->Low->Close or Open->Low->High->Close). Default policy: assume Open->High->Low->Close for upward moves; allow this behavior to be pluggable in `IFillModel`.

Partial fills
- Initial MVP: treat fills as atomic per order (full or none). Future work: support partial fills by volume model.

Commission & slippage
- `ICommissionModel` should support per-order fixed, per-share, or percent-of-notional.
- `ISlippageModel` should support fixed ticks, percent-of-price, or liquidity-based slippage.

## Capital competition algorithm (multiple entry signals same bar)

1. Collect all new entry `OrderRequest`s for the bar. Each request includes a `Priority` (numeric) and desired sizing input (either explicit quantity or risk parameters).
2. Sort requests by `Priority` descending (tie-breaker by symbol or time).
3. For each request in order:
   - Use `ISizingModel` to compute required notional and quantity (taking into account minimum lot sizes and pyramid rules).
   - Check `IRiskModel` constraints: available cash (after reserved), per-symbol heat cap, portfolio heat, max position size, max pyramids.
   - If the order fits constraints: reserve cash/margin and accept the order.
   - If it does not fit but can be downscaled (partial sizing allowed): compute scaled size and accept if above minimum.
   - Otherwise: reject or queue the order (policy-driven).

Rationale: this deterministic allocator ensures the portfolio enforces a global capital budget rather than letting strategies independently assume infinite capital.

## Position and pyramiding model

- `Position` aggregates multiple entry `Trade` legs and tracks per-leg metadata.
- Configurable `MaxPyramids` and per-leg sizing rules.
- `Position.Metadata` (dictionary or typed generic) to store strategy state: `PyramidCount`, `HighestCloseSinceEntry`, `BreakoutDate`, `TrendStrength`, `CustomFlags`.
- On each bar `Position` gets an `Update(bar)` callback to refresh dynamic stop/take targets and update metadata.

## Risk, Portfolio Heat & Constraints

- Heat definitions:
  - `SymbolHeat = abs(notional_exposure(symbol)) / equity` (percent)
  - `PortfolioHeat = sum(abs(notional_exposure_all)) / equity` (gross exposure)
- Configurable parameters: `MaxSymbolHeatPercent`, `MaxPortfolioHeatPercent`, `MaxSinglePositionPercent`, `MaxLeverage`.
- `IRiskModel` evaluates new orders against these limits and returns accept/scale/reject decisions.

## Equity curve, trade history & performance

- Persist an `EquitySnapshot` at each bar: timestamp, cash, unrealized P&L, realized P&L, total equity.
- Record `Trade` objects for each execution leg and for position exits (aggregated trade-level view).
- Performance statistics to compute:
  - Net P&L, Gross P&L, Win rate, Average win/loss, Expectancy, Profit factor
  - Max drawdown (and drawdown duration), CAGR/annualized return, Sharpe ratio, Sortino ratio
  - Exposure metrics (time in market, percent exposure by symbol)

Formulas and exact implementations should be included in a Performance helper module and covered by unit tests.

## Visualization (HTML)

- Export JSON files with:
  - Price series and annotated trades (entry/exit markers), per-symbol.
  - Equity curve time series and drawdown series.
- Provide a minimal `viz/index.html` that uses Chart.js or Plotly to load the JSON and render interactive charts with zoom and tooltips.

## Testing strategy

- Unit tests (xUnit):
  - Portfolio accounting: cash, reserved cash, unrealized/realized P&L after fills and price moves.
  - Order execution: market/limit/stop fills according to OHLC heuristics, commission & slippage application.
  - Pyramiding: multiple entry legs and correct `PyramidCount` and aggregated P&L.
  - Capital competition: multiple signals on same bar with constrained cash — highest priority orders accepted until constraints hit.
  - Risk checks: per-symbol heat and portfolio heat enforcement.
  - Performance metrics: verify Sharpe, max drawdown, win rate against deterministic trade sets.

- Integration tests:
  - Run a small strategy over a known dataset and assert final equity and summarized trade counts.
  - Visualization export produces expected JSON schema.

## Example minimal API (conceptual)

- `IStrategy.OnBar(MarketSlice slice, PortfolioSnapshot portfolio) -> IEnumerable<OrderRequest>`
- `OrderRequest` includes: Symbol, Side, OrderType, Price (optional), RiskParameters or Quantity, Priority, ClientMetadata.

## Implementation milestones (mapping to TODOs)

1. Write plan (this file).
2. Create solution and project skeleton; add core domain types and Candle loader.
3. Implement `MarketData` synchronizer and simple in-memory feed.
4. Implement `Portfolio` and `Position` types with metadata support and equity snapshots.
5. Implement `BrokerSimulator` and `IFillModel` with default OHLC heuristics and market/limit/stop support.
6. Implement `ISizingModel`, `ICommissionModel`, `ISlippageModel`, and `IRiskModel` with default strategies.
7. Implement `Engine` event loop and a simple demo `MovingAverageCrossStrategy` (strategy only emits orders).
8. Add unit tests for accounting, fills, pyramiding and capital competition.
9. Add equity curve export and `viz/index.html`.
10. Polish README and examples.

## Future extensions

- Tick-level execution and microstructure models.
- Margin and futures support (per-instrument margin models, initial/maintenance margins).
- More advanced matching/market impact models and partial fills.
- Live-trading adapter and broker connector (kept separate via interfaces).
