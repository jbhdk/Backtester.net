# backtester.net.yahoo

A [Yahoo Finance](https://finance.yahoo.com) historical market-data provider for the
[backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package so the core engine takes on no implicit network dependency: only consumers
who fetch from Yahoo pull it in. It depends only on the .NET base class library — no third-party SDK.

## What it is

`YahooHistoricalDataProvider` implements the engine's `IHistoricalDataProvider` seam, fetching OHLCV
bars from Yahoo Finance's v8 chart JSON API and mapping them to the engine's `Candle` type. Like every
provider it is **pure acquisition** — no caching, no disk — so it slots in wherever the Alpaca or CSV
providers do and lets `HistoricalDataFetcher` handle the cache.

## Quick start

```csharp
// Online + cached: calls Yahoo only for bars the local CSV cache lacks.
IHistoricalDataFetcher fetcher = new HistoricalDataFetcher(new YahooHistoricalDataProvider());
IReadOnlyList<Candle> candles = await fetcher.FetchAsync("AAPL", fromUtc, toUtc, "1d");
```

The provider takes an optional `HttpClient`, so you can supply your own (configured handler, shared
instance, or a stub in tests):

```csharp
IHistoricalDataProvider provider = new YahooHistoricalDataProvider(httpClient);
```

## Intervals

Pass the interval string straight through to Yahoo. Supported values:

`1m`, `2m`, `5m`, `15m`, `30m`, `60m`, `1h`, `1d`, `1wk`, `1mo`

Intraday history is limited by Yahoo (roughly the last ~730 days for hourly, less for finer
intervals). An unsupported interval throws `NotSupportedException` before any network call.

## Behavior notes

- **Raw, unadjusted prices.** Yahoo's v8 `quote` block is as-traded OHLC, so a stock split appears as
  an overnight price gap. If split-adjusted data matters for your study, prefer the Alpaca provider
  (`backtester.net.alpaca`), which defaults to split-adjusted bars.
- **Holiday gaps are skipped.** Rows where open or close is null are dropped.
- **Bars are returned sorted ascending by timestamp.**
- **Transport errors surface** as `InvalidOperationException` carrying the HTTP status and body.
