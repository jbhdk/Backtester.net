# backtester.net

A bar-by-bar backtesting engine for financial market strategies. It steps through historical
candles one bar at a time, lets a strategy emit orders, simulates broker fills, and tracks a
portfolio and performance. This document fixes the ubiquitous language for the engine.

## Language

### Market data

**Bar**:
One OHLCV interval of price for a symbol. The engine advances one bar at a time.
The .NET type is `Candle`.
_Avoid_: tick, candlestick (in prose), period.

**Slice**:
All symbols' bars at a single timestamp (`MarketSlice`). The unit the engine processes per step.
_Avoid_: frame, snapshot (snapshot means the portfolio view).

**History**:
The bars at or before the current bar, made available so a strategy can compute indicators.
Reading indicator values aligned to the current bar is lookahead-free because indicators are causal.
_Avoid_: lookahead, window (window is an indicator parameter).

### Data acquisition

**Provider**:
A source adapter that fetches bars from an external service (e.g. Yahoo's v8 chart API).
Pure acquisition: it performs no caching and touches no disk. The .NET seam is
`IHistoricalDataProvider`.
_Avoid_: feed, client, source, fetcher (the fetcher caches; the provider never does).

**Fetcher**:
The cache-aware orchestrator that serves bars from the local Cache and calls a Provider only
for the bars the Cache lacks. The .NET seam is `IHistoricalDataFetcher`.
_Avoid_: provider, loader, repository.

**Cache**:
The on-disk copy of previously fetched bars for one symbol-and-interval. The Fetcher reads and
writes it; the Provider is unaware of it.
_Avoid_: store, database.

**Freshness window**:
The maximum age of the Cache's most recent bar within which the Fetcher trusts the Cache and does
not contact the Provider. Age is measured against the requested end of range, or now when that end
is in the future — whichever is earlier. So a completed historical window stays fresh indefinitely,
while a run ending at the present goes stale as time passes. Bounded at one week: the current
week's bars are not required.
_Avoid_: TTL, expiry.

### Orders & execution

**Order**:
A working instruction to buy or sell (`Market`, `Limit`, or `Stop`). Orders are **resting** —
they persist across bars until filled or cancelled (GTC).
_Avoid_: trade, transaction.

**Next-bar fill**:
An order submitted while processing bar N is evaluated against bar N+1 (Market at N+1's open;
Stop/Limit at their trigger if N+1's range crosses it). This is the engine's anti-lookahead rule.
_Avoid_: same-bar fill, immediate fill.

**Bracket**:
An entry order with an attached stop-loss and take-profit that form an OCO group.
_Avoid_: OTO, parent/child order.

**OCO** (one-cancels-other):
A group of orders in which one filling automatically cancels the siblings. Prevents the
stop-loss and take-profit both filling in the same bar.
_Avoid_: bracket-cancel, linked orders.

**Fill**:
A single execution of an order at a price, producing a `Trade` record.
_Avoid_: execution (in prose).

### Positions & accounting

**Position**:
The net holding in a symbol. **Long-only** in this engine: quantity is never negative; a Sell
may only reduce or close a long.
_Avoid_: short, holding, lot.

**Trade**:
The record of one fill (the `Trade` type). NOT a complete trade in the trader's sense.
_Avoid_: using "trade" for an entry-to-exit cycle — that is a Round trip.

**Round trip**:
A complete entry-to-exit cycle for a position, carrying realized PnL and bars held. The unit
of per-trade performance analytics.
_Avoid_: trade, deal, position close.

**Realized equity** (cost-basis equity):
Cash plus the cost basis of open positions (`Cash + Σ AveragePrice·Quantity`); excludes
unrealized PnL. Equals cash when flat. The base for risk sizing.
_Avoid_: equity (unqualified), book value.

**Marked equity**:
Cash plus open positions marked to the latest close (`Cash + Σ Close·Quantity`); includes
unrealized PnL. The basis of the equity curve.
_Avoid_: equity (unqualified), NAV.

### Risk & sizing

**Risk-per-trade sizing**:
Position size chosen so a stop-out loses a fixed fraction of realized equity:
`shares = floor(RiskFraction · Equity / StopDistance)`.
_Avoid_: notional sizing, percent sizing.

**Stop distance**:
The per-share loss if the stop is hit: `|entry − stopPrice|`.
_Avoid_: risk, spread.

### Execution models

**Execution model**:
A pluggable rule the broker applies when simulating execution — commission, slippage,
position sizing, or risk. The four families live in `Backtester.ExecutionModels`. In this
codebase, **"model" always means one of these**; nothing else is a model.
_Avoid_: using "model" for a strategy, an indicator, or a data type (e.g. a Slice).

### Strategy & indicators

**Strategy**:
The decision logic. Receives full History on `OnStart`, then `OnBar` per bar, and acts via the
broker (`Submit`, `SubmitBracket`, `Cancel`, `Modify`).
_Avoid_: algo, system, model (model means an execution model).

**Indicator**:
A derived series (EMA, ATR, Keltner, …). The engine is **indicator-agnostic**: it ships none and
takes no indicator dependency; the consumer brings their own library.
_Avoid_: signal, study.

### Performance

**Performance stats**:
Aggregate metrics computed from round trips and the equity curve (win rate, profit factor,
expectancy, max drawdown, CAGR, Sharpe, …).
_Avoid_: results, report.

**Max drawdown**:
The largest peak-to-trough decline in marked equity over the run.
_Avoid_: loss, drop.
