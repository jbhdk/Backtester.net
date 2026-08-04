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

Pass the shared `m`/`h`/`d`/`wk`/`mo` interval vocabulary used by the Yahoo and Alpaca providers, plus
an Oanda-specific `s` (seconds) unit. It is parsed into Oanda's fixed granularity codes:

- Seconds: `5s`, `10s`, `15s`, `30s`
- Minutes: `1m`, `2m`, `4m`, `5m`, `10m`, `15m`, `30m`
- Hours: `1h`, `2h`, `3h`, `4h`, `6h`, `8h`, `12h`
- `1d`, `1wk`, `1mo`

Oanda's daily/weekly/monthly granularities have no multiplier, so only `1d`, `1wk`, and `1mo` are
valid — `7d` throws just like `7m`, `5h`, or `20s` would. An unsupported interval throws
`NotSupportedException` naming the valid set, before any network call.

## Price component (Mid / Bid / Ask)

The constructor's `priceComponent` parameter selects which side of the spread candles are read from,
via `PriceComponent.Mid` (default), `.Bid`, or `.Ask`. It maps to Oanda's `price` query parameter
(`M`/`B`/`A`) and the response sub-object OHLC values are read from (`mid`/`bid`/`ask`):

```csharp
IHistoricalDataProvider askSide = new OandaHistoricalDataProvider(apiToken, priceComponent: PriceComponent.Ask);
```

`Mid` defaults so a forex backtest behaves like a single-price-series backtest, comparable to how the
equity providers report a single price. See [ADR 0028](../docs/adr/0028-oanda-provider-defaults.md)
for why this is the default rather than `Bid`/`Ask`.

## Environment (Practice / Live)

The constructor's `environment` parameter selects the Oanda host, via `OandaEnvironment.Practice`
(default) or `.Live`:

```csharp
IHistoricalDataProvider live = new OandaHistoricalDataProvider(apiToken, environment: OandaEnvironment.Live);
```

`Practice` resolves to `https://api-fxpractice.oanda.com`, `Live` to
`https://api-fxtrade.oanda.com`. Both serve identical real market candles — Oanda's Practice
environment is not simulated data — so `Practice` is the free, no-funding-required default; a user
with an existing live account's token opts into `Live` explicitly. See
[ADR 0028](../docs/adr/0028-oanda-provider-defaults.md).

## Pagination

Oanda's candles endpoint caps each response at 5000 candles and offers no page-token concept. A
range wider than that is walked automatically: the provider requests a chunk, and if the response is
full it advances `from` to just after the last returned candle's timestamp and requests again,
continuing until a short response is seen or the requested `to` is reached. All chunks are
concatenated and returned sorted ascending — the caller sees one seamless range regardless of how
many requests it took, matching `AlpacaHistoricalDataProvider`'s existing page-walking behavior.

## Currency conversion

A pair whose quote currency differs from your account's own currency needs an `Instrument` declaring
which Oanda pair carries the conversion rate and which way that pair is quoted. `OandaInstrument.For`
works both out for you, so you never hand-write currency metadata and can never forget a
`ConversionSymbol` or pick the wrong rate direction:

```csharp
Instrument[] instruments =
{
    OandaInstrument.For("EUR_USD", accountCurrency: "JPY"),   // -> ConversionSymbol USD_JPY, Multiply
    OandaInstrument.For("EUR_GBP", accountCurrency: "JPY"),   // -> ConversionSymbol GBP_JPY, Multiply
};

Portfolio portfolio = new Portfolio(startingCash: 1_000_000m, accountCurrency: "JPY", instruments);
```

A pair that already quotes in your account currency (`USD_JPY` in a JPY account) comes back with no
`ConversionSymbol` and nothing extra to fetch. A symbol that isn't an Oanda pair — `EURUSD`, `AAPL` —
is rejected with an `InstrumentDeclarationException` naming it, at declaration time rather than
mid-run.

The factory knows Oanda's pair-ordering convention, which is what decides whether the rate is divided
or multiplied by: Oanda names `GBP_USD` but `USD_JPY`, so a GBP-quoted symbol in a USD account
multiplies while a JPY-quoted one divides. A currency the factory's ordering table doesn't list is
assumed to be one Oanda quotes second (below every listed currency, above `JPY`, which Oanda quotes
second against everything). That holds for every instrument Oanda lists today, but if you trade a
newly-added exotic and the inferred pair looks wrong, declare that one `Instrument` by hand:

```csharp
Instrument instrument = new()
{
    Symbol = "USD_XYZ", QuoteCurrency = "XYZ", ConversionSymbol = "XYZ_USD",
    ConversionOperation = ConversionOperation.Multiply
};
```

Hand the Instruments to the `Portfolio` and nothing else: it is the single hand-off point, and the
`Engine` — which takes only the tradable symbol list — derives the conversion series to fetch from the
Portfolio's own declarations.

The provider itself stays pure acquisition — it has no concept of the account currency, and the
factory is a separate declaration-time helper that performs no I/O. See the root
[README](../README.md#multi-currency--forex-accounting),
[ADR 0029](../docs/adr/0029-instrument-and-multi-currency-forex-accounting.md) and
[ADR 0031](../docs/adr/0031-currency-converter-module.md) for how `Engine` fetches and `Portfolio`
applies the conversion.

## Behavior notes

- **`Volume` is a tick count, not traded volume.** Forex spot is decentralized, so there is no
  consolidated traded-volume figure; Oanda's `volume` field counts price ticks observed during the
  candle, and that count is mapped directly into `Candle.Volume`. Treat it as an activity proxy, not
  a trade size. See [ADR 0028](../docs/adr/0028-oanda-provider-defaults.md).
- **Bars are returned sorted ascending by timestamp.**
- **Transport errors surface** as `InvalidOperationException` carrying the HTTP status and body,
  unwrapped, with no retry/backoff.
