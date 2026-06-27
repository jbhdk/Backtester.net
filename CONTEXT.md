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

**Bracket level**:
The current trigger price of a bracket's protective leg — its stop-loss or take-profit. It is a value
that can **move** over the life of a position: a trailed stop's level steps as the strategy modifies
it, so a leg's level over a round trip is a series, not a single number. Distinct from **Stop
distance**, which is the fixed per-share risk measured from entry to the stop at sizing time.
_Avoid_: stop price / target price (unqualified — those name the leg, not its evolving level), line.

**Fill**:
A single execution of an order at a price, producing a `Trade` record.
_Avoid_: execution (in prose).

### Positions & accounting

**Position**:
The net holding in a symbol, as a **signed** quantity: positive is **long**, negative is **short**,
zero is flat. A single fill never flips the sign — an order opposite to the open position reduces it
and clamps at zero (any overshoot is discarded); reversing direction takes a second order from flat.
_Avoid_: holding, lot.

**Short**:
A position with negative quantity, opened by a Sell from flat (selling shares not held) and closed by
a Buy. Realized PnL on close is `(entry − exit)·quantity` — the mirror of a long.
_Avoid_: short-sell (as a noun), naked.

**Cover**:
Buying to close or reduce a short — the Buy-side mirror of selling to close a long.
_Avoid_: buy-to-close, unwind.

**Trade**:
The record of one fill (the `Trade` type). NOT a complete trade in the trader's sense.
_Avoid_: using "trade" for an entry-to-exit cycle — that is a Round trip.

**Round trip**:
A complete entry-to-exit cycle for a position, carrying realized PnL and bars held. The unit
of per-trade performance analytics. Either direction: a long round trip pairs a Buy entry with a
Sell exit; a short round trip pairs a Sell entry with a Buy exit. A round trip is **realized the
moment a fill reduces or closes the position** — a partial exit realizes a round trip for the closed
portion and the position lives on. The Portfolio is its source: it emits each round trip as it
closes, and a strategy may **observe** them live to react to its own results (e.g. pause after a run
of losses). What a strategy does with that result is its own decision; the engine carries on either
way.
_Avoid_: trade, deal, position close.

**Exit reason**:
Why a Round trip closed, as one of three values. **Take-profit**: the bracket's target (Limit) leg
filled. **Stop-loss**: the bracket's stop leg filled, including a trailed stop (a trailed stop is
still a stop-loss, not a separate reason). **Signal**: the position was closed by a non-bracket order
the strategy submitted — a deliberate strategy exit or the flattening leg of a reversal.
_Avoid_: manual, trailing-stop (as a distinct reason), end-of-run (an open position never becomes a
round trip, so it has no exit reason).

**Realized equity** (cost-basis equity):
Cash plus the cost basis of open positions (`Cash + Σ AveragePrice·Quantity`, Quantity signed so a
short contributes negative cost basis); excludes unrealized PnL. Equals cash when flat. The base for
risk sizing.
_Avoid_: equity (unqualified), book value.

**Marked equity**:
Cash plus open positions marked to the latest close (`Cash + Σ Close·Quantity`, Quantity signed so a
short's value falls as price rises); includes unrealized PnL. The basis of the equity curve and of
buying power.
_Avoid_: equity (unqualified), NAV.

### Risk & sizing

**Risk-per-trade sizing**:
Position size chosen so a stop-out loses a fixed fraction of realized equity:
`shares = floor(RiskFraction · Equity / StopDistance)`.
_Avoid_: notional sizing, percent sizing.

**Stop distance**:
The per-share loss if the stop is hit: `|entry − stopPrice|`.
_Avoid_: risk, spread.

### Margin

**Margin account**:
The account operates on Reg-T **initial** margin: opening or adding to a position commits margin
rather than full cash. Longs require 50% of notional, shorts 150%. Margin is *held* against buying
power, not debited from cash — a short sale credits cash by its full proceeds. Only initial margin is
modelled; there is no maintenance margin and the engine never force-liquidates, so a runaway loss can
drive marked equity negative and the run simply reports it.
_Avoid_: cash account, leverage (as the model name).

**Initial margin**:
The equity an order must commit to open or add to a position: `rate · |price · quantity|`, rate 0.5
long / 1.5 short. A reducing order commits none and releases the closed portion's margin.
_Avoid_: margin requirement (unqualified), maintenance margin.

**Buying power**:
Marked equity above the initial margin already committed by open positions
(`MarkedEquity − Σ held initial margin`). An order is accepted only if its initial margin does not
exceed buying power. Always enforced by the account — it is **not** a pluggable execution model.
_Avoid_: margin (unqualified), excess equity.

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
A derived market calculation a strategy exposes for visualization (a moving average, RSI, MACD, …).
It groups **one or more** Indicator series under a single name and a single placement: overlaid on
the price scale, or in its own separate pane that all of its series share. A single-line indicator
(e.g. a moving average) has one series; a MACD is one indicator in a separate pane grouping three
series — its MACD line, its signal line, and its histogram. The engine stays **indicator-agnostic in
computation**: it ships none and takes no indicator dependency (ADR 0003); the consumer brings their
own library and computes the values. The engine may, however, be *aware* of the indicators a strategy
chooses to expose and surface them for the report (ADR 0007) — awareness is not a dependency.
_Avoid_: signal, study.

**Indicator series**:
One named, time-aligned line within an Indicator (e.g. MACD's "Signal" line), distinct from the
private computation a strategy performs to make decisions. Placement (price overlay vs separate pane)
belongs to the parent Indicator, not the series; a series carries only its name, its values, and its
shape — a line, a filled area, or a histogram. The strategy computes it; the engine surfaces it; the
consumer renders it. Exposure is opt-in — a strategy that exposes nothing is still valid.
_Avoid_: plot, overlay (an overlay is a placement of the parent indicator, not the series itself).

### Performance

**Performance stats**:
Aggregate metrics computed from round trips and the equity curve (win rate, profit factor,
expectancy, max drawdown, CAGR, Sharpe, …).
_Avoid_: results, report.

**Max drawdown**:
The largest peak-to-trough decline in marked equity over the run.
_Avoid_: loss, drop.

**Per-symbol stats**:
Performance stats computed for a single symbol in isolation, for the report's per-symbol column.
Trade metrics come from that symbol's round trips.
_Avoid_: per-ticker results.

**Isolated equity**:
A single symbol's equity curve, defined as if that symbol alone traded the **full** starting
capital: `starting capital + the symbol's own realized + unrealized PnL` at each bar. The basis for a
symbol's per-symbol max drawdown, CAGR, and Sharpe. For a single-symbol run it equals the portfolio's
marked equity exactly. Per-symbol isolated curves do **not** sum to the portfolio curve.
_Avoid_: symbol equity (unqualified), allocated equity.
