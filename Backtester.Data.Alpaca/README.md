# backtester.net.alpaca

An [Alpaca](https://alpaca.markets) historical market-data provider for the
[backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package so the core engine takes on no `Alpaca.Markets` SDK dependency: only
consumers who fetch from Alpaca pull it in.

## What it is

`AlpacaHistoricalDataProvider` implements the engine's `IHistoricalDataProvider` seam, fetching US
equity OHLCV bars from Alpaca and mapping them to the engine's `Candle` type. Like every provider it
is **pure acquisition** — no caching, no disk — so it slots in wherever the Yahoo or CSV providers do
and lets `HistoricalDataFetcher` handle the cache.

## Quick start

```csharp
// From Alpaca API credentials (US equities; consolidated SIP + split-adjusted by default):
IHistoricalDataProvider provider = new AlpacaHistoricalDataProvider(keyId, secret);

HistoricalDataFetcher fetcher = new(provider);
IReadOnlyList<Candle> candles = await fetcher.FetchAsync("AAPL", fromUtc, toUtc, "1d");
```

Prefer to build (or share, or fake) the underlying Alpaca client yourself? Inject it instead:

```csharp
IAlpacaDataClient client = Environments.Live.GetAlpacaDataClient(new SecretKey(keyId, secret));
IHistoricalDataProvider provider = new AlpacaHistoricalDataProvider(client);
```

For historical market data, `Environments.Live` and `Environments.Paper` resolve to the same Alpaca
data endpoint — the paper/live split only matters for trading — so there is no environment to choose.

## Feed and adjustment

Both default to the values that make a bar-by-bar backtest correct, and both are overridable:

| Option | Default | Why |
| --- | --- | --- |
| `MarketDataFeed` | `Sip` | Consolidated tape across all US exchanges — the prices and volumes a backtest should simulate against. |
| `Adjustment` | `SplitsOnly` | Removes split price-cliffs (which fabricate round trips and trip stops) while keeping traded price levels. |

```csharp
// Free-tier accounts have no SIP entitlement — opt into the IEX feed explicitly:
IHistoricalDataProvider provider = new AlpacaHistoricalDataProvider(keyId, secret, MarketDataFeed.Iex);

// Total-return series (splits and dividends):
IHistoricalDataProvider total = new AlpacaHistoricalDataProvider(
    keyId, secret, MarketDataFeed.Sip, Adjustment.SplitsAndDividends);
```

The default is deliberate: requesting `Sip` without entitlement fails loudly with a
`RestClientErrorException` rather than silently serving the thinner IEX feed. Acting on a clear
error beats discovering distorted results later.

## Intervals

The interval string is parsed (not table-matched) into an Alpaca `BarTimeFrame`, sharing the Yahoo
provider's vocabulary so you can swap providers without rewriting intervals. Leading digits give the
multiple, the suffix the unit:

- `m` minutes, `h` hours, `d` days, `wk` weeks, `mo` months
- arbitrary multiples work: `5m`, `15m`, `2h`, `1d`, `1wk`, `1mo`

Anything it cannot parse throws `NotSupportedException` before any network call.

## Behavior notes

- **Pagination is handled internally.** Alpaca returns a single capped page per call; the provider
  walks the `NextPageToken` until the range is exhausted, requesting the maximum page size, with no
  cap on the total number of bars returned.
- **Errors surface unwrapped.** Alpaca's typed `RestClientErrorException` (carrying the HTTP status
  and error code) propagates as-is; the provider never catches and rewraps it.
- **Bars are returned sorted ascending by timestamp**, mapping plain OHLCV and dropping Alpaca's
  `Vwap` and `TradeCount`.
