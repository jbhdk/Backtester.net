# Backtester.Data — Historical Data Fetcher

This folder contains the historical-data fetching and local cache utilities used by the backtester.

Summary
- Purpose: fetch OHLCV history from pluggable providers and store per-symbol+interval CSV files under the repo `data/` folder for fast reuse.
- Key components:
  - `IHistoricalDataProvider` — provider contract for fetching candles.
  - `CsvBarLoader` — local CSV read/write/append+merge utilities.
  - `HistoricalDataFetcher` — orchestrator that decides whether to use cached data or fetch missing ranges. Also implements `IDataPrimer`.
  - `CsvHistoricalDataFetcher` — offline `IHistoricalDataFetcher` that reads candles straight from a committed `{SYMBOL}_{interval}.csv` file (no provider, no cache logic) for deterministic, repeatable runs.
  - `IDataPrimer` — a separate seam (kept off `IHistoricalDataFetcher` for ISP) for **priming**: warming the cache for a wide range up front so later runs over sub-ranges never touch the network.
  - `CoverageFloorLoader` — reads/writes the per-symbol+interval **coverage-floor** sidecar (`{SYMBOL}_{interval}.meta.json`).
  - `DataCoverageException` — thrown when a run's start precedes the coverage floor (see below).

Cache files
- Location: repo root `data/` folder by default (configurable via `HistoricalDataFetcher` constructor).
- File naming: `{SYMBOL}_{interval}.csv` (examples: `AAPL_1h.csv`, `SPY_1d.csv`). Each cache file has a small companion sidecar `{SYMBOL}_{interval}.meta.json` holding the coverage floor (see below); the CSV itself stays a clean, importable OHLCV file.
- CSV schema (header row included):

  ```
  Timestamp,Open,High,Low,Close,Volume
  2026-06-14T09:00:00Z,150.12,151.00,149.90,150.80,123456
  ```

- Timestamp format: ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`). Daily files may be `yyyy-MM-dd` compatible but are written using ISO 8601.

Behavior & policy
- Freshness window (the recent edge): a non-empty cache is trusted when its most recent bar is within 7 days of the requested end — measured against the requested `to`, or now when that is in the future, whichever is earlier. So a completed historical window stays fresh indefinitely, while a run ending at the present goes stale as time passes (see [ADR 0006](../../docs/adr/0006-cache-freshness-over-completeness.md)).
- Empty cache: the fetcher fetches the full requested range, persists it, and establishes the coverage floor at the requested `from`.
- Stale cache: the fetcher extends the **tail** from the latest cached bar (`latest..to`) and merges the result; it never lowers the coverage floor.
- Merge policy: when duplicates by `Timestamp` occur the later occurrence (the appended row) wins and replaces earlier values.
- Intervals: initial implementation targets hourly (`1h`) OHLCV and daily; provider support varies. If a provider cannot supply the requested interval or symbol it must throw an informative exception — the fetcher will propagate this error (no automatic fallback between providers).

Coverage floor & priming
- **Coverage floor** — the earliest range start ever asked of the provider for a symbol+interval, recorded in the `.meta.json` sidecar. It records what was *asked*, not what was *returned*, so it distinguishes a late listing (data legitimately starts after `from`) from an under-fetch (that window was never requested). See [ADR 0021](../../docs/adr/0021-coverage-floor-and-priming.md).
- **Front-edge guard** — a run whose `from` precedes an existing floor is refused with a `DataCoverageException` (carrying the symbol, requested `from`, floor, and interval) rather than served a silently short slice. A legacy cache with **no** sidecar is trusted as before; it gains a floor the next time the fetcher calls the provider.
- **Priming** — `IDataPrimer.PrimeAsync(symbols, from, to, interval)` warms the cache for a wide range **without running a backtest**: it wholesale-fetches `[from, to]` per symbol (concurrently), merges into the cache, and lowers the floor. Prime the wide range once, then run in-sample and out-of-sample sub-ranges served entirely from the cache — no network, no coverage exception.

```csharp
// Warm 2020→now once, then run sub-ranges offline against the cache.
HistoricalDataFetcher fetcher = new(provider, dataFolder: "data");
await fetcher.PrimeAsync(new[] { "AAPL", "MSFT" }, new DateTime(2020, 1, 1), DateTime.UtcNow, "1d");
// in-sample and out-of-sample runs now hit the warm cache; an un-primed earlier start throws DataCoverageException.
```

Provider notes
- Implement `IHistoricalDataProvider.FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken)`.
- Live network providers ship as their own opt-in packages, so the core stays network-free (see `docs/adr/0009-network-providers-separate-packages.md`): `backtester.net.yahoo` (Yahoo Finance v8) and `backtester.net.alpaca` (Alpaca). The only provider in the core package is the offline `CsvHistoricalDataFetcher`. If a provider cannot serve the requested interval it throws `NotSupportedException`.

Concurrency & safety
- Writes are atomic: CSV writes are performed to a temp file then copied to the target path to reduce corruption risk. This implementation does not implement cross-process locking — coordinate multiple processes externally if necessary.

Testing
- Unit tests live in `BacktesterTests/Data.Tests/`:
  - `CsvBarLoaderTests` — verifies read/write, append+merge and deduplication behavior.
  - `HistoricalDataFetcherTests` — verifies new-file fetch, fresh-cache use, stale-cache append, provider error propagation, coverage-floor establishment, and the front-edge guard.
  - `CsvHistoricalDataFetcherTests` — verifies reading a known CSV, deterministic repeat calls, and empty result when no file exists.
  - `DataPrimerTests` — verifies priming establishes/lowers the coverage floor (monotonic-down), dedups merged bars, primes multiple symbols, and lets a subsequent run be served from the warm cache.
  - `DataCoverageExceptionTests` — verifies the exception message names priming as a remedy.

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
