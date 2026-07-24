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

**Prime**:
To populate the Cache for a range of bars ahead of any backtest, so later runs over sub-ranges are
served entirely from the Cache without contacting the Provider. Distinct from a Fetch, which happens
as part of a run and self-heals a stale tail; a Prime is a deliberate up-front warm of a wide range.
_Avoid_: preload, warm, seed, cache (as a verb).

**Coverage floor**:
The earliest range start the Fetcher has ever requested from the Provider for one symbol-and-interval.
Below it — for a `from` earlier than the floor — the Cache's lack of bars is *unknown* (that window was
never requested), so the Fetcher refuses the run rather than serve a silently short slice. At or above
it, a missing bar is *known* to not exist at the source (e.g. before a late listing) and the Cache is
trusted. It is a front-edge low-water mark only; the recent edge remains the Freshness window's concern.
_Avoid_: coverage range, completeness, start date, earliest bar (the floor is what was *asked*, not what
was *returned*).

### Run windows

**Data range**:
The span of bars a single run pulls from the Fetcher and hands to the strategy's History — the Test
range plus any earlier Warmup. Its end coincides with the Test range's end; only its start may reach
further back. Distinct from a Prime, which warms the Cache across many runs — the Data range is one
run's own reach into that Cache.
_Avoid_: fetch range, lookback, full history, window.

**Test range**:
The span of bars a run actually steps through: the loop iterates it, every Performance stat is measured
over it, and the report shows it. It sits within the Data range; the Warmup bars ahead of it feed
History only. Because the loop, the accounting, and the report all follow the Test range, a run's
results are confined to it by construction — nothing is clipped after the fact.
_Avoid_: backtest range (every run is a backtest), measured window, sample, in-sample / out-of-sample
(those name a workflow role a Test range plays, not the range itself).

**Warmup**:
The stretch of bars immediately before the Test range, included in the Data range so a strategy's
indicators are already valid on the first Test bar. Optional and caller-chosen — as a period, an
absolute start, or a bar count. Warmup bars reach the strategy's History only: they are never looped,
so they produce no orders, fills, round trips, or equity points. Over-provisioning is harmless; asking
for more bars than the Cache holds above its Coverage floor is refused rather than served short.
_Avoid_: burn-in (implies running-but-not-measuring; Warmup is not looped at all), priming (a Prime
warms the Cache, a Warmup deepens one run's History), lookback, seasoning.

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

**Initial risk**:
The currency a round trip stood to lose if its **entry** stop had been hit, before any trailing:
`Stop distance at entry · Quantity`. Fixed at entry; a trailed **Stop level** moving later does not
change it. Undefined for a round trip that entered without a protective stop.
_Avoid_: risk (unqualified), current risk, stop-out amount.

**R-multiple**:
A round trip's realized profit expressed in units of its **Initial risk**:
`RealizedPnL / Initial risk`. `+2R` is a win of twice the risked amount; `−1R` is a full stop-out
loss. Defined only when Initial risk is (the round trip entered with a stop).
_Avoid_: R (unqualified in prose), reward-to-risk (that is a forward-looking target ratio, not a
realized outcome).

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

### Analysis

**Analysis**:
A machine-generated critique of one run: a short summary plus a list of Findings, rendered as its own
report section. It is **commentary, not measurement** — it interprets the Performance stats and round
trips, it never produces a number the report could not already show. Like configuration, it is
caller-supplied: the report never generates it.
_Avoid_: performance stats, results, review, insight.

**Finding**:
One observation about a run paired with the change it recommends. Carries a **category** (what area of
the run it concerns), a **severity**, the **observation** (what the numbers show), and the
**recommendation** (what to change). Observation and recommendation are separate on purpose: evidence
must be stated before a prescription is made. A finding may also be a **strength** — something the run
does well — which is not a low-severity problem.
_Avoid_: issue, suggestion, insight, signal.

**Analysis digest**:
The deliberately reduced view of a run handed to an Analyzer. It carries the run context, the
Performance stats, the per-symbol stats, the round trips, the rejected orders, and the caller's
configuration — and deliberately omits candles and indicator series, which are for the reader's eye,
not for interpretation. Its size is bounded by round-trip count: a run with more round trips than the
digest admits is **rejected**, not silently sampled, unless the caller asks for sampling — and a
sampled digest says so within itself, so the Analysis is never mistaken for a whole-run conclusion.
_Avoid_: prompt, payload, context, summary.

**Analyzer**:
The orchestrator that turns a run into an Analysis: it builds the Analysis digest, asks an Analysis
client, and validates what comes back. It owns the whole contract — the digest, the instructions, and
the required output shape — so that an Analysis reads the same whichever AI produced it. It is
**AI-agnostic** and makes no outbound call itself.
_Avoid_: reviewer, critic, agent.

**Analysis client**:
The adapter for one AI service (Claude, for instance). It carries the Analyzer's request
to that service and returns the raw answer; it decides nothing about what is asked or what is
acceptable. Deliberately **not** called a Provider — a Provider fetches bars.
_Avoid_: provider, model (model means an execution model), backend, vendor.

**Analysis contract**:
The fixed output shape every Analysis client's answer must satisfy. Enforced by the Analyzer, not
trusted from the AI: an answer that names an unknown severity, omits a recommendation, or is not
well-formed is a **violation** and is rejected, never repaired or coerced. A run gets a valid Analysis
or none at all — a partially-understood Analysis would leave the reader unable to tell which parts the
AI actually produced.
_Avoid_: schema (unqualified), format, response.

### Optimization

**Optimization**:
The produced artifact of sweeping a strategy's Parameters to find the best configuration by an
Objective: the ranked Trials plus the best one. The noun names the *result*, the way **Analysis** names
the critique artifact — not the process.
_Avoid_: sweep (as the result), search, tuning, grid (grid is the search method).

**Optimizer**:
The orchestrator that runs an Optimization: it expands the Parameter ranges into a Parameter space,
runs a backtest per Parameter set, scores each by the Objective, and ranks them. Parallels the
**Analyzer**.
_Avoid_: tuner, searcher, sweeper, solver.

**Parameter**:
A strategy's tunable input that an Optimization varies (e.g. a moving average's window, an ATR stop
multiple). Orthogonal to a **Setting**: the same property may carry both a Parameter range and a
`[ReportSetting]` — "Parameter" names the concern that it *can vary*, while configuration/Setting names
how a run's inputs are *rendered* in the report. What varies is a Parameter; how it is shown is a
Setting.
_Avoid_: setting (for the varying concern), knob, variable, argument.

**Parameter space**:
Every Parameter set an Optimization will evaluate: the cartesian product of each varied Parameter's
range (from/to/step). v1 evaluates the space **exhaustively** — a grid search — with no pluggable
search-method seam; a seam is added later, deliberately, only when a second method (random, walk-forward)
arrives.
_Avoid_: grid (as the space itself; grid names the exhaustive method), search space, domain.

**Parameter set**:
One complete assignment of values to the varied Parameters — a single point in the Parameter space.
Not yet evaluated; a Trial is a Parameter set once a backtest has scored it.
_Avoid_: combination, combo, configuration (configuration is the report's view of a run's inputs).

**Trial**:
One Parameter set evaluated by a backtest, carrying its Performance stats and its Score. An
Optimization is a set of Trials; the best Trial is the winner. A Trial wraps a single backtest — "run"
stays the informal word for that underlying backtest.
_Avoid_: candidate, run (for the scored unit), sample, iteration.

**Objective**:
The rule an Optimization ranks Trials by: a function over a Trial's **combined** (whole-run)
Performance stats paired with a direction (maximize or minimize), e.g. maximize Sharpe or minimize
Max drawdown. It reads the combined stats only — never Per-symbol stats — so ranking is always on
whole-run performance.
_Avoid_: fitness, goal, target, metric (a metric is one Performance stat; the Objective is the rule).

**Score**:
The single number an Objective assigns to a Trial — the value Trials are ranked by. Higher wins when
the Objective maximizes, lower when it minimizes.
_Avoid_: fitness, objective value, rank, result.

**Eligibility**:
Whether a Trial is allowed to be the winner. A Trial is **eligible** only if it meets the
Optimization's minimum round-trip count; a Trial below it is **ineligible** and can never be the best,
so a lucky handful of round trips cannot top the ranking. An ineligible Trial is still ranked and
shown — flagged as ineligible — never silently dropped. Mirrors the Analysis stance of rejecting
rather than hiding.
_Avoid_: filter, disqualified, valid/invalid.
