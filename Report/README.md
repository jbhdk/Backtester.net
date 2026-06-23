# backtester.net.report

Opt-in HTML reporting for the [backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package so the core engine takes on no reporting or web-asset dependencies.

## View-model builder

`ReportModelBuilder` is a pure function from a run's `BacktestResult` to a serializable
`ReportModel`. It performs no I/O, and every value the page renders — including the run inputs,
which the `BacktestResult` now carries — is derived from the result alone:

- **Stats** — net profit (currency and percent), CAGR, max drawdown, Sharpe, trades, win rate,
  profit factor, expectancy, average win/loss, max consecutive losses.
- **Round trips** — number, symbol, entry/exit time and price, quantity, P&L, plus derived
  **Return %** `(Exit − Entry) / Entry` and compact **Time Held** (e.g. `5d 6h`).
- **Run** — symbols, interval, date range, starting equity, and derived final equity and total return %.
- **Per-symbol candles**, **indicator series** with pane placement, and the portfolio **equity curve**.

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
- **Win rate** — fraction of round trips that were profitable.
- **Profit factor** — gross profit divided by absolute gross loss; zero when there are no losses.
- **Expectancy** — expected value per trade: `WinRate × AvgWin + (1 − WinRate) × AvgLoss`.
- **Avg R** — average R multiple: expectancy expressed in units of the average losing trade (the loss stands in for per-trade risk, as no stop is modelled).
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

### Trade duration

- **Avg duration** — mean holding time across all round trips.
- **Median duration** — median holding time across all round trips.
- **Longest trade** — longest holding time of any round trip.
- **Shortest trade** — shortest holding time of any round trip.

### Exposure & capital

- **Market exposure** — fraction of bars on which at least one position was open.
- **Avg capital** — time-weighted average gross capital deployed in open positions across all bars (flat bars count as zero), in currency.
- **Max capital** — peak gross capital deployed in open positions on any single bar, in currency.

### Run context

- **Symbols** — the symbols traded in the run.
- **Interval** — the bar interval.
- **From** / **To** — the run's date range.
- **Starting equity** — cash the run began with.
- **Final equity** — equity at the end of the run.
