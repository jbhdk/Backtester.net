# Alpaca provider lives in a separate package

The `AlpacaHistoricalDataProvider` is an `IHistoricalDataProvider` like the Yahoo and CSV providers,
but it ships in its own project and NuGet package, `Backtester.Data.Alpaca`, which references
`Backtester` — rather than living alongside the others in `Backtester\Data`. It depends on the
third-party `Alpaca.Markets` SDK and constructs its bars by mapping the SDK's `IBar` page stream
onto `Candle`; the engine is unaffected, since it still consumes the same `IHistoricalDataProvider`
seam.

The driver is dependency isolation. `Backtester` is a published library (`GeneratePackageOnBuild`),
and every provider already in `Backtester\Data` (Yahoo, CSV) depends only on the BCL. Putting the
Alpaca provider in `Backtester` would force `Alpaca.Markets` and its transitive dependencies onto
every consumer of `backtester.net`, including those who never touch Alpaca. A separate package keeps
the core engine vendor-free and lets only Alpaca users opt into the SDK.

## Considered options

- **Drop `AlpacaHistoricalDataProvider.cs` into `Backtester\Data`** alongside Yahoo and CSV — the
  simplest change and faithful to the existing "all providers live together" pattern. Rejected: it
  bleeds a vendor SDK into the core package's dependency graph for all consumers, which is the cost
  Yahoo avoids by being BCL-only.

## Consequences

- The "all providers live in `Backtester\Data`" pattern no longer holds; a provider with a
  third-party dependency earns its own package. A future BCL-only provider can still go in
  `Backtester\Data`.
- Composing an Alpaca-backed run means referencing both `backtester.net` and
  `Backtester.Data.Alpaca`; the provider is then wired into `HistoricalDataFetcher` exactly like any
  other.
- The two packages version and pack independently, mirroring the existing `Report` package split.
