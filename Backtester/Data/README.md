# Backtester.Data — Historical Data Fetcher

This folder contains the historical-data fetching and local cache utilities used by the backtester.

Summary
- Purpose: fetch OHLCV history from pluggable providers and store per-symbol+interval CSV files under the repo `data/` folder for fast reuse.
- Key components:
  - `IHistoricalDataProvider` — provider contract for fetching candles.
  - `YahooHistoricalDataProvider` — concrete provider (supports `1d`, `1wk`, `1mo` and intraday `1h`/`60m` with limited history).
  - `CsvBarLoader` — local CSV read/write/append+merge utilities.
  - `HistoricalDataFetcher` — orchestrator that decides whether to use cached data or fetch missing ranges.

Cache files
- Location: repo root `data/` folder by default (configurable via `HistoricalDataFetcher` constructor).
- File naming: `{SYMBOL}_{interval}.csv` (examples: `AAPL_1h.csv`, `SPY_1d.csv`).
- CSV schema (header row included):

  ```
  Timestamp,Open,High,Low,Close,Volume
  2026-06-14T09:00:00Z,150.12,151.00,149.90,150.80,123456
  ```

- Timestamp format: ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`). Daily files may be `yyyy-MM-dd` compatible but are written using ISO 8601.

Behavior & policy
- Freshness window: if the latest candle in cache is within the last 7 days and the cache covers the requested date range, the fetcher returns the cached rows without network access.
- If cache is missing or does not cover the requested `from..to` range, the fetcher requests the missing range from the configured `IHistoricalDataProvider` and appends (merge) results into the CSV.
- Merge policy: when duplicates by `Timestamp` occur the later occurrence (the appended row) wins and replaces earlier values.
- Intervals: initial implementation targets hourly (`1h`) OHLCV and daily; provider support varies. If a provider cannot supply the requested interval or symbol it must throw an informative exception — the fetcher will propagate this error (no automatic fallback between providers).

Provider notes
- Implement `IHistoricalDataProvider.FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken)`.
- `YahooHistoricalDataProvider` uses Yahoo Finance CSV download and supports `1d`, `1wk`, `1mo`, and `1h`/`60m` (intraday history limited to approx. 730 days). If the provider cannot serve the requested interval it will throw `NotSupportedException`.

Concurrency & safety
- Writes are atomic: CSV writes are performed to a temp file then copied to the target path to reduce corruption risk. This implementation does not implement cross-process locking — coordinate multiple processes externally if necessary.

Testing
- Unit tests live in `BacktesterTests/Data.Tests/`:
  - `CsvBarLoaderTests` — verifies read/write, append+merge and deduplication behavior.
  - `HistoricalDataFetcherTests` — verifies new-file fetch, fresh-cache use, stale-cache append, and provider error propagation.

Usage example
- Create a provider (e.g., `new YahooHistoricalDataProvider()`), then:

```csharp
var fetcher = new HistoricalDataFetcher(provider, dataFolder: "..\\data");
var candles = await fetcher.FetchAsync("AAPL", fromUtc, toUtc, "1h");
```

Configuration
- `HistoricalDataFetcher` accepts an optional `dataFolder` path. For provider APIs that require keys (e.g., Alpha Vantage) pass provider-specific options via the provider constructor or external configuration.

Roadmap
- Add additional providers (Alpha Vantage, Tiingo, etc.) that implement `IHistoricalDataProvider` for broader intraday support and API-key handling.
- Add optional cross-process locking for shared cache directories.
