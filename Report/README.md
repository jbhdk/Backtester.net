# backtester.net.report

Opt-in HTML reporting for the [backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package so the core engine takes on no reporting or web-asset dependencies.

## View-model builder

`ReportModelBuilder` is a pure function from a run's `BacktestResult` to a serializable
`ReportModel`. It performs no I/O, and every value the page renders — including the run inputs,
which the `BacktestResult` now carries — is derived from the result alone:

- **Stats** — net profit (currency and percent), net profit split by direction, CAGR, max drawdown,
  Sharpe, trades, win rate, profit factor, expectancy, average win/loss, max consecutive losses, and
  the leverage and margin utilization the run carried.
- **Round trips** — number, symbol, entry/exit time and price, quantity, the trip's **Leverage** and
  **Margin** (see below), P&L, plus derived
  **Return %** `(Exit − Entry) / Entry`, the **Initial stop** and **Initial target** (the entry-time
  stop-loss and take-profit levels, frozen at entry and unaffected by later trailing; a dash when the
  entry declared no such leg — Initial stop shows exactly when **R** does, Initial target only when a
  bracket target armed), the **Exit reason** (`Take-profit`, `Stop-loss`, or `Signal`), and compact
  **Time Held** (e.g. `5d 6h`).
- **Run** — symbols, interval, date range, starting equity, and derived final equity and total return %.
- **Per-symbol candles**, **indicators** (each grouping one or more series in a shared pane), and the portfolio **equity curve**.

```csharp
ReportModel model = new ReportModelBuilder().Build(result);
string json = System.Text.Json.JsonSerializer.Serialize(model);
```

`System.Text.Json` (in-box on net8.0) is sufficient for serialization — no external dependency.

## HTML report

`HtmlReportWriter` turns the model into a single self-contained HTML file that opens from `file://`
with no external dependencies. It serializes the model to JSON and token-replaces it into an
embedded `template.html` (real HTML/CSS/JS, not C# string-building), inlining the data:

```csharp
// One-call path straight from a run — the writer projects the result internally:
new HtmlReportWriter().Write(result, "report.html");

// Or supply a pre-built model (e.g. when you also want the JSON):
new HtmlReportWriter().Write(model, "report.html");

// Either form is also available without touching disk:
string html = new HtmlReportWriter().BuildHtml(result);
```

The page renders a grouped stats panel (headline, trade-quality, run context) with money as
currency, ratio stats as percentages, and P&L colour-coded green/red, plus a sortable round-trips
table. Displayed P&L is gross of commission and slippage.

Every money figure is in the portfolio's `AccountCurrency`; entry and exit **prices** stay in the
instrument's own quote currency, so a cross-currency run shows the real price the pair traded at
alongside account-currency P&L (see the engine's
[multi-currency accounting](https://github.com/jbhdk/Backtester.net#multi-currency--forex-accounting)).

## Optimization report

This package also carries the **Optimization** report — a separate report that renders a whole Parameter
sweep as a sortable leaderboard with a Score heatmap. Its serializable model (`OptimizationReportModel`)
and writer (`OptimizationHtmlReportWriter`) live here so that the
[`backtester.net.optimization`](https://www.nuget.org/packages/backtester.net.optimization) package can
project into them without this package ever depending on it (the same arrangement as `ReportAnalysis`).
The Optimization package builds the model and drives the writer; see its README for the full flow.

## Stat reference

The stats panel is split into cards. Each card column shows the value for **All symbols** (the
portfolio) and, when a symbol is selected on the chart, that symbol alone.

### Performance

- **Net profit** — net profit after commissions and slippage, in currency.
- **Net profit %** — net profit as a fraction of starting equity. Per symbol this is the symbol's profit over the *whole-portfolio* starting equity, i.e. its contribution to the portfolio return — not a return on the capital deployed in that symbol alone.
- **Buy & hold** — return of an equal-weight buy-and-hold of all traded symbols over the run; the benchmark, not the strategy. The per-symbol value is that symbol's price return divided by the number of benchmark symbols, i.e. its equal-weight *contribution* to the benchmark, so it sits on the same whole-portfolio capital base as Net profit % and the per-symbol values sum to the portfolio figure. (A true per-symbol return on its own deployed capital would need per-symbol equity curves, which the engine does not yet produce.)
- **CAGR** — compound annual growth rate.
- **Sharpe** — annualised Sharpe ratio (daily bars, risk-free rate = 0): mean bar return over its standard deviation.
- **Sortino** — like Sharpe but divided by downside deviation only, so upside volatility is not penalised.

### Drawdown & recovery

- **Max drawdown** — largest peak-to-trough decline in marked equity, as a fraction.
- **Avg drawdown** — mean depth of all drawdown episodes, as a fraction.
- **Drawdown length** — duration of the longest drawdown episode (peak to recovery, or to run end if never recovered).
- **Time to recover** — time from the deepest drawdown's trough back to a new equity high (zero if never recovered).
- **Recovery factor** — net profit divided by the maximum drawdown in currency; higher means more profit for less peak-to-trough pain.
- **Calmar** — CAGR divided by the maximum drawdown fraction.

### Trade quality

- **Trades** — number of completed round trips.
- **Winners / Break-even / Losers** — count of round trips that closed with a positive, exactly-zero, or negative P&L. These sum to **Trades**. A non-zero break-even count is why **Win rate**, **Avg win**, and **Avg loss** alone cannot reproduce **Expectancy** (see below).
- **Win rate** — fraction of round trips that were profitable.
- **Profit factor** — gross profit divided by absolute gross loss; zero when there are no losses.
- **Expectancy** — expected value per trade: the mean realized P&L across all round trips (`NetProfit ÷ Trades`). This equals `WinRate × AvgWin + (1 − WinRate) × AvgLoss` only when there are no break-even trades; otherwise that formula over-weights the average loss, because `1 − WinRate` includes the break-even trips while `AvgLoss` is averaged over losers only.
- **Avg R** — average R multiple: the mean of per-trade R (`RealizedPnL ÷ InitialRisk`) across the round trips that declared an entry stop. No-stop trips carry no R and are excluded from both the sum and the count (not counted as 0R, which would drag the mean toward zero); the value is a dash when no trip has a defined initial risk. _This meaning changed from prior reports_, where "Avg R" was a proxy — expectancy expressed in units of the average losing trade.
- **Avg win** — average profit of winning round trips.
- **Avg loss** — average loss of losing round trips (negative).
- **Median trade** — median realized P&L across all round trips.

### Wins & losses

- **Largest win** — largest single winning round trip's profit.
- **Largest loss** — largest single losing round trip's loss (negative).
- **Max consec. wins** — longest consecutive run of winning round trips.
- **Max consec. losses** — longest consecutive run of losing round trips.
- **Profitable long** — fraction of long round trips that were profitable.
- **Profitable short** — fraction of short round trips that were profitable.
- **Net profit long** — realized net profit of the long round trips, in currency.
- **Net profit short** — realized net profit of the short round trips, in currency. With **Net profit long** it partitions **Net profit** exactly (every round trip is one direction or the other).

### Trade duration

- **Avg duration** — mean holding time across all round trips.
- **Median duration** — median holding time across all round trips.
- **Longest trade** — longest holding time of any round trip.
- **Shortest trade** — shortest holding time of any round trip.

### Exposure & capital

- **Market exposure** — fraction of bars on which at least one position was open.
- **Avg capital** — time-weighted average gross capital deployed in open positions across all bars (flat bars count as zero), in currency.
- **Max capital** — peak gross capital deployed in open positions on any single bar, in currency.
- **Avg leverage** — average leverage (gross exposure `Σ|position value|` over marked equity) across bars that held a position; flat bars are excluded, so the figure reads "how levered when in the market" rather than blending with time out of it (that is **Market exposure**). `1.0x` is fully invested and unlevered.
- **Peak leverage** — the highest single-bar leverage reached over the run.
- **Avg margin** — average margin utilization (committed Reg-T initial margin over marked equity) across bars that held a position, as a fraction. This is the same committed margin that gates buying power, re-marked each bar.
- **Peak margin** — the highest single-bar margin utilization; near 100% means open positions had nearly exhausted buying power.

> **Per-symbol leverage and margin are understated.** The per-symbol column measures each symbol against its **isolated equity**, which assumes that symbol alone traded the *full* starting capital. A symbol that in reality shared the account's capital therefore shows lower leverage and margin utilization in its own column than it actually contributed to the portfolio — the same caveat that already applies to per-symbol drawdown and CAGR. The **All symbols** column is the true portfolio figure.

The per-round-trip table also carries two entry-time columns derived from the same idea:

- **Leverage** (round-trip column) — the trip's entry notional (`EntryPrice × Quantity`) over the marked equity when it opened; a dash when that equity was non-positive.
- **Margin** (round-trip column) — the Reg-T initial margin the trip committed at entry: its side's rate (0.5 long / 1.5 short) times its entry notional, in currency. Unlike the aggregate **Avg/Peak margin**, this per-trip figure is frozen at the entry notional rather than re-marked.

> **Two per-trip figures are not yet cross-currency-aware.** The **Margin** column recomputes from the account's Reg-T rates over the trip's *native* entry notional, so for an `Instrument` that declares its own `MarginRate` (e.g. 2% for 50:1 forex leverage) or quotes in another currency it is not the margin the account actually committed — the aggregate **Avg/Peak margin** stats, which come from the portfolio's own committed margin, are correct. **R multiple** (and the **Avg R** stat) divides an account-currency `RealizedPnL` by a native-currency `InitialRisk`, mixing units for a symbol quoted in another currency. Single-currency runs on Reg-T margin are unaffected; making the report consume the engine's converted figures is a separate piece of work.

### Run context

- **Symbols** — the symbols traded in the run.
- **Interval** — the bar interval.
- **From** / **To** — the run's date range.
- **Starting equity** — cash the run began with.
- **Final equity** — equity at the end of the run.
