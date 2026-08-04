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

**Next-bar fill** (timing):
An order submitted while processing bar N is evaluated against bar N+1 — never bar N itself. This is
the engine's anti-lookahead rule. Names *when* an order is matched; **Gap-aware fill** names at what
price. One scoped exception: a bracket's protective leg is armed by its entry's fill, and if that leg
is **already marketable at the arming bar's open** (an adverse or favourable gap put the fill through
the leg's price), it fills on that same arming bar rather than resting to the next — it is marketable
the instant it exists, so filling it is not lookahead. A live bracket behaves the same way: the entry
fills and an already-triggered protective leg executes right after, for a same-bar (often near-scratch)
round trip. A leg the arming bar merely trades *through* later keeps the ordinary next-bar timing.
_Avoid_: same-bar fill (as a blanket term; the exception is narrow), immediate fill.

**Gap-aware fill** (pricing):
A triggered order never fills at a price better than the bar's open. A Market fills at the open; a
Stop or Limit fills at its trigger unless the bar gapped past it, in which case the open. So a gap
through a stop fills *worse* than the stop and a gap past a limit fills *better* than the limit — the
strategy is never credited a price the bar's open did not offer. The rule is geometric: a
below-market trigger (a Buy limit or a Sell stop) fills at `min(trigger, open)`, an above-market
trigger (a Sell limit or a Buy stop) at `max(trigger, open)`.
_Avoid_: trigger-price fill, exact-stop fill.

**Bracket**:
An entry order with one or two attached protective legs — a stop-loss and/or a take-profit. When
both are present they form an OCO group (one filling cancels the other); with a single leg there is
no sibling to cancel and the lone leg simply rests until filled or the position is closed by Signal.
A Bracket must have at least one leg — an entry with neither is a plain Order, not a Bracket. Each
leg is expressed either as an **absolute** price or as a fill-relative **offset** (a Stop distance /
target offset the engine resolves against the actual fill when the entry fills) — one form per leg;
setting both for the same leg is caller misuse.
_Avoid_: OTO, parent/child order.

**OCO** (one-cancels-other):
A group of orders in which one filling automatically cancels the siblings. Prevents the
stop-loss and take-profit both filling in the same bar. Applies only to a two-legged Bracket; a
single-leg Bracket forms no OCO group.
_Avoid_: bracket-cancel, linked orders.

**Bracket level**:
The current trigger price of a bracket's protective leg — its stop-loss or take-profit. It is a value
that can **move** over the life of a position: a trailed stop's level steps as the strategy modifies
it, so a leg's level over a round trip is a series, not a single number. Distinct from **Stop
distance**, which is the fixed per-share risk measured from entry to the stop at sizing time.
_Avoid_: stop price / target price (unqualified — those name the leg, not its evolving level), line.

**Initial stop**:
The entry-time level of a round trip's stop-loss — the first **Bracket level** its stop takes, frozen
at open-from-flat and unchanged by any later trailing (the same anchor as **Initial risk**). It is the
*any declared entry stop*: an armed bracket stop leg, or the sizing stop of a risk-sized entry that
armed no bracket. Null when the entry declared no stop — a target-only bracket or a plain entry.
_Avoid_: stop price (unqualified), current stop, stop-loss level (that is the evolving Bracket level).

**Initial target**:
The entry-time level of a round trip's take-profit — the first **Bracket level** its target takes,
frozen at open-from-flat. A target exists only through a bracket (there is no sizing target), so it is
null for a stop-only bracket or a plain entry.
_Avoid_: target price (unqualified), current target, take-profit level (that is the evolving Bracket
level).

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
short contributes negative cost basis), each position's cost basis translated into the Account
currency; excludes unrealized PnL. Equals cash when flat. The base for risk sizing.
_Avoid_: equity (unqualified), book value.

**Marked equity**:
Cash plus open positions marked to the latest close (`Cash + Σ Close·Quantity`, Quantity signed so a
short's value falls as price rises), each position's value translated into the Account currency;
includes unrealized PnL. The basis of the equity curve and of buying power.
_Avoid_: equity (unqualified), NAV.

### Instruments & currency

**Instrument**:
Caller-supplied per-symbol metadata: its quote currency, the Conversion symbol and Conversion
operation needed to translate that currency into the Account currency, and an optional per-instrument
margin rate. Handed to the Portfolio — the single hand-off point — which cross-checks every
declaration against the Account currency: a quote currency is required on each Instrument, one
differing from the account's requires a Conversion symbol, one equal to it forbids one. Supplied only
for symbols that need it: a stock/ETF run on Reg-T margin in the account's own currency declares none
at all. Deliberately left room to carry other per-symbol execution config later (e.g. per-instrument
spread/commission).
_Avoid_: symbol (a symbol is `Instrument.Symbol`, a bare identifier), ticker.

**Account currency**:
The single currency Portfolio's cash and equity are denominated in, set once at construction. Every
Instrument's quote currency is compared against it to decide whether conversion applies.
_Avoid_: base currency (a currency-pair term, not the account's), currency (unqualified).

**Conversion symbol**:
The exact symbol an Instrument declares for fetching the historical rate that converts its quote
currency into the Account currency (e.g. an Instrument quoted in JPY names `USD_JPY` when the account
is USD). Null when the Instrument's quote currency already equals the Account currency. The named pair
may be quoted in either direction; the Instrument's Conversion operation declares which. Provider
symbol-naming is the caller's concern — the engine fetches whatever string it is given and stays
ignorant of any provider's naming convention.
_Avoid_: cross rate, conversion pair (that names the currency pair conceptually; Conversion symbol is
the concrete fetch key).

**Conversion operation**:
Whether translating a native amount into the Account currency divides or multiplies by the Conversion
symbol's rate, determined by which way that pair is quoted: a pair whose first currency is the Account
currency divides (`USD_JPY` in a USD account — JPY per USD), one whose first currency is the quote
currency multiplies (`GBP_USD` — USD per GBP). Declared on the Instrument alongside its Conversion
symbol; Divide is the default. An Instrument factory sets it automatically, so a caller using one
never chooses.
_Avoid_: rate quotation, inversion, direction (unqualified).

**Currency converter**:
The module that translates quote-currency amounts into the Account currency: it holds each
Instrument's conversion declaration and cross-checks it against the Account currency at construction,
observes Conversion-symbol closes as bars arrive, and applies the Conversion operation. Identity for
an Instrument declaring no conversion; a declared conversion with no observed rate is refused loudly,
never silently left unconverted. Its timing rule: a fill
translates at the conversion pair's previous close — the last rate honestly known without lookahead —
while an end-of-bar mark uses the current close. Owned by the Portfolio, which is the single hand-off
point for Instruments.
_Avoid_: exchange (a venue), FX engine, rate service.

**Instrument factory**:
A Provider-package convenience that builds a fully-populated Instrument from that provider's own
symbol naming — inferring the quote currency (the second currency of a forex pair), the Conversion
symbol, and its Conversion operation — so a caller never hand-writes currency metadata. Each factory
lives with its Provider because pair-naming conventions are provider-specific; the engine never parses
a symbol.
_Avoid_: symbol parser, instrument resolver.

### Risk & sizing

**Risk-per-trade sizing**:
Position size chosen so a stop-out loses a fixed fraction of realized equity:
`shares = floor(RiskFraction · Equity / StopDistance)`. Its risk scales with the account.
_Avoid_: notional sizing, percent sizing.

**Fixed-risk sizing**:
Position size chosen so a stop-out loses a fixed **currency amount** that does **not** scale with the
account: `shares = floor(RiskAmount / StopDistance)`. The sibling of Risk-per-trade sizing — same Stop
distance denominator, but a constant numerator instead of `RiskFraction · Equity`, so it reads no
equity. The .NET type is `FixedRiskSizing`, its amount `RiskAmount`.
_Avoid_: fixed-dollar sizing (the language is currency-neutral), fixed-size (that is a fixed share
count, `FixedSizeModel`), notional sizing.

**Stop distance**:
The per-share loss if the stop is hit: `|entry − stopPrice|`. A bracket can express it two ways: as
an **absolute** stop price (resolved against the strategy's pre-fill reference, and so vulnerable to a
gap between decision and fill), or as a fill-relative **offset** — a distance the engine subtracts from
(long) or adds to (short) the actual fill at fill time, making the realized Stop distance equal the
requested offset exactly regardless of any gap. The target leg has a mirror **target offset**, but a
target feeds no risk, so it is not a Stop distance.
_Avoid_: risk, spread.

**Initial risk**:
The Account-currency amount a round trip stood to lose if its **entry** stop had been hit, before any
trailing: `Stop distance at entry · Quantity`, translated at the rate in force when the trip opened —
what a broker would have told you was at risk as you entered. Fixed at entry; neither a trailed **Stop
level** nor a later rate move changes it. Undefined for a round trip that entered without a protective
stop.
_Avoid_: risk (unqualified), current risk, stop-out amount.

**R-multiple**:
A round trip's realized profit expressed in units of its **Initial risk**:
`RealizedPnL / Initial risk`. `+2R` is a win of twice the risked amount; `−1R` is a full stop-out
loss. Defined only when Initial risk is (the round trip entered with a stop). Both sides are Account
currency, each translated at its own moment — risk at entry, profit at exit — so for a cross-currency
trip a rate move between the two shows up in R, as it did in the account.
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
The equity an order must commit to open or add to a position: `rate · |price · quantity|`, at the
Instrument's own symmetric rate when it declares one, else the Reg-T split (0.5 long / 1.5 short). A
reducing order commits none and releases the closed portion's margin.
_Avoid_: margin requirement (unqualified), maintenance margin.

**Entry notional**:
The capital a round trip committed when it opened, in the Account currency at the rates in force as it
filled — so a trip that scaled in across a rate move carries what actually left the account, not a
blended price re-translated afterwards. The numerator of the trip's **Leverage** and the base its
**Initial margin** was taken on.
_Avoid_: exposure (that is Market exposure, a time fraction), position size, entry cost.

**Buying power**:
Marked equity above the initial margin already committed by open positions
(`MarkedEquity − Σ held initial margin`). An order is accepted only if its initial margin does not
exceed buying power. Always enforced by the account — it is **not** a pluggable execution model.
_Avoid_: margin (unqualified), excess equity.

**Leverage**:
The ratio of gross market exposure to marked equity — how much position the account carries per unit of
its own capital, gross value so a short counts positive (`Σ|position value| / marked equity`). Aggregated
over a run as **Peak leverage** (the highest single-bar value) and **Avg leverage** (the mean over bars
that held a position; flat bars are excluded, so the figure is not diluted by time out of the market —
that dilution is **Market exposure**'s job). Per **Round trip** it is that trip's **Entry notional**
over marked equity on its entry bar. 1.0 is fully invested and unlevered; above
1.0 the account carries exposure beyond its own equity (borrowing long, or short proceeds).
_Avoid_: gearing, exposure (unqualified — that is Market exposure, a time fraction), margin (leverage is
notional-to-equity, not the committed requirement).

**Margin utilization**:
The fraction of marked equity tied up as committed initial margin: `Σ committed margin / marked equity`.
Aggregated as **Peak margin** and **Avg margin** (the latter over exposed bars only, as with **Leverage**).
It climbs toward and past 100% as open positions consume the account; its currency complement — the
head-room left — is **Buying power**. Reads the same committed margin the account holds to gate buying
power: the initial-margin rate applied to each position's *current* marked value (the engine models no
separate maintenance margin). Its per-**Round trip** sibling column instead freezes the margin at the
trip's **Entry notional**, taken at that Instrument's own rate — the margin the trade committed when it
opened.
_Avoid_: margin (unqualified), leverage (that is notional-to-equity), buying power (the currency head-room,
not the used fraction).

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

**Net profit long / Net profit short**:
The realized PnL of a run's long **Round trips** and of its short round trips, taken separately, in
currency. Because every round trip is one direction or the other, the two partition **Net profit**
exactly — they sum to it with nothing left over. A directional attribution of the same realized result,
not a new measure.
_Avoid_: long/short PnL (unqualified), directional return (these are currency, not a ratio).

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

**Rejected trial**:
A Trial whose configuration the code under test refused to run: building or running its backtest raised a
configuration rejection, so the Trial carries no Performance stats and no Score — only its Parameter set
and the rejection's reason. It is still shown on the leaderboard as a full row, ranked below every scored
Trial, and can never be the winner. Distinct from **Eligibility**: an ineligible Trial ran and was scored
but closed too few round trips; a Rejected trial never produced a result at all. Only configuration
rejections are captured this way — any other failure stops the sweep rather than becoming a row.
_Avoid_: failed trial, faulted trial, invalid combination (the Parameter space still contains the set;
the code under test rejects it), error.

**Eligibility**:
Whether a Trial is allowed to be the winner. A Trial is **eligible** only if it meets the
Optimization's minimum round-trip count; a Trial below it is **ineligible** and can never be the best,
so a lucky handful of round trips cannot top the ranking. An ineligible Trial is still ranked and
shown — flagged as ineligible — never silently dropped. Mirrors the Analysis stance of rejecting
rather than hiding.
_Avoid_: filter, disqualified, valid/invalid.
