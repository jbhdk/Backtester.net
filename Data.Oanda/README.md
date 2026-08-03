# backtester.net.data.oanda

An [Oanda](https://www.oanda.com) v20 historical forex market-data provider for the
[backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package so the core engine takes on no implicit network dependency: only consumers
who fetch from Oanda pull it in. It depends only on the .NET base class library — no third-party SDK.

## What it is

`OandaHistoricalDataProvider` implements the engine's `IHistoricalDataProvider` seam, fetching OHLCV
candles from Oanda's v20 `/v3/instruments/{instrument}/candles` endpoint and mapping them to the
engine's `Candle` type. Like every provider it is **pure acquisition** — no caching, no disk — so it
slots in wherever the Alpaca or Yahoo providers do and lets `HistoricalDataFetcher` handle the cache.

This first cut is a tracer bullet: it targets the Practice environment, requests Mid-price candles,
and fetches a single page (up to 5000 candles). Pagination, price-component switching, and
environment switching arrive in later issues.

## Quick start

```csharp
IHistoricalDataProvider provider = new OandaHistoricalDataProvider(apiToken);

HistoricalDataFetcher fetcher = new(provider);
IReadOnlyList<Candle> candles = await fetcher.FetchAsync("EUR_USD", fromUtc, toUtc, "1h");
```

The provider takes an optional `HttpClient`, so you can supply your own (configured handler, shared
instance, or a stub in tests):

```csharp
IHistoricalDataProvider provider = new OandaHistoricalDataProvider(httpClient, apiToken);
```

No account ID is required — only a bearer API token.

## Symbol

The `symbol` parameter is passed through verbatim as Oanda's `{instrument}` path segment, e.g.
`EUR_USD`. No parsing or translation is performed.

## Intervals

Pass the shared `m`/`h`/`d`/`wk`/`mo` interval vocabulary used by the Yahoo and Alpaca providers; it
is parsed into Oanda's fixed granularity codes:

- Minutes: `1m`, `2m`, `4m`, `5m`, `10m`, `15m`, `30m`
- Hours: `1h`, `2h`, `3h`, `4h`, `6h`, `8h`, `12h`
- `1d`, `1wk`, `1mo`

Oanda's daily/weekly/monthly granularities have no multiplier, so only `1d`, `1wk`, and `1mo` are
valid — `7d` throws just like `7m` or `5h` would. An unsupported interval throws
`NotSupportedException` naming the valid set, before any network call.

## Behavior notes

- **Practice environment, Mid price.** This provider always targets
  `https://api-fxpractice.oanda.com` and always requests `price=M` (Mid) candles.
- **`Volume` is a tick count, not traded volume.** Forex spot is decentralized, so there is no
  consolidated traded-volume figure; Oanda's `volume` field counts price ticks observed during the
  candle, and that count is mapped directly into `Candle.Volume`. Treat it as an activity proxy, not
  a trade size.
- **Single page only.** This issue does not walk pagination; requests beyond Oanda's 5000-candle page
  limit are a separate issue.
- **Bars are returned sorted ascending by timestamp.**
- **Transport errors surface** as `InvalidOperationException` carrying the HTTP status and body,
  unwrapped, with no retry/backoff.
