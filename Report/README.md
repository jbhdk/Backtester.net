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
