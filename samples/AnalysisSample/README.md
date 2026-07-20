# Analysis sample

A complete run that ends in a report containing an **Analysis**: it backtests a moving-average cross
over four ETFs, builds the report model, attaches the configuration cards, asks Claude, and writes
`analysis-report.html`.

This is a sample, not a test. It is not referenced by the test suite, and no test in this repository
depends on an API key or on network access.

## Running it

```bash
setx ANTHROPIC_API_KEY "sk-ant-..."        # Windows; export on Linux/macOS
dotnet run --project samples/AnalysisSample
```

The bars come from the committed CSV files in [`samples/data`](../data), read through
`CsvHistoricalDataFetcher`, so the backtest is deterministic and needs no data provider. **The call to
Claude is the sample's only outbound call, and `ANTHROPIC_API_KEY` is its only prerequisite.**

## What it demonstrates

The call site, whose ordering is the part worth copying — see
[`Program.cs`](Program.cs):

1. Build the `ReportModel` from the `BacktestResult`.
2. Attach the configuration cards **before** analysing. The Analysis digest carries them, and the
   Analyzer cannot comment on a setting it was never shown.
3. Await the Analyzer and attach the resulting `Analysis` to the model.
4. Write the report from the model.

It also shows what a failure costs. `AnalyzeAsync` throws rather than returning a repaired Analysis —
a malformed answer, a digest over the round-trip cap, or a service that could not be reached are each
caught separately, and each leaves `model.Analysis` null. The report is written either way: a null
Analysis simply renders no Analysis section.

The model asked is `claude-sonnet-5`. The documented floor is Sonnet-class.
