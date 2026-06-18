# Engine owns data fetching

Originally the engine consumed a public `IMarketDataFeed`, built by a `MarketDataSynchronizer`
factory (with an inner `InMemoryMarketDataFeed`) that the consumer had to assemble and inject.
The feed exposed a bar-by-bar cursor (`Advance`, `GetCurrentSlice`, `GetLookback`, `CurrentTime`,
`GetFullHistory`), and the provider-based factory fetched from a raw `IHistoricalDataProvider` —
silently bypassing the disk cache in `HistoricalDataFetcher`. The consumer was forced to learn
two synchronisation types that are really engine internals.

We decided the engine fetches its own data. `Engine` now takes an `IHistoricalDataFetcher`
(plus the symbols, range, and interval to fetch) and `StartAsync()` fetches every symbol
concurrently through the cache, synchronises the per-symbol series into a `MarketSlice` stream,
and runs the loop. The public feed abstraction is gone: `IMarketDataFeed`,
`MarketDataSynchronizer`, and `InMemoryMarketDataFeed` are deleted, and the outer-join + forward-fill
synchronisation moves into an `internal SliceSequence` that the engine iterates.

This trades strict single-responsibility — the engine now performs network and disk I/O instead
of being handed ready-made data — for an ergonomic surface: the consumer wires a fetcher and runs,
without ever meeting a synchroniser or a feed cursor. It also fixes the cache bypass, since the
engine now goes through `HistoricalDataFetcher` rather than a raw provider.

## Consequences

- `Engine.StartAsync()` replaces the synchronous `Start()` on `IEngine`; consumers await the run.
- Tests and in-memory consumers supply data by faking `IHistoricalDataFetcher` (synthetic candles,
  no disk or network), the same seam pattern already used for `IHistoricalDataProvider`. The
  `CreateFromSeries` test path is no longer needed.
- `SliceSequence` is internal and exercised through the engine's public behaviour; it must not be
  surfaced or covered via `InternalsVisibleTo`.
- `MarketSlice` is a shared boundary DTO (Engine, Broker, Portfolio), so it lives in
  `Backtester.Core`, not behind the engine — the engine *constructs* it but does not *own* its type.
- `GetLookback` was dead and is removed; strategies still receive full History via `OnStart`,
  which the engine now passes straight from the fetched series.
- A future contributor wanting a streaming or live feed should reintroduce a data-source seam
  deliberately, not resurrect the old public feed cursor.
