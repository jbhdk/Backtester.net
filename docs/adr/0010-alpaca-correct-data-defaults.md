# Alpaca provider defaults to correct data, failing loud

`AlpacaHistoricalDataProvider` exposes the Alpaca market-data feed and price adjustment as
constructor parameters, and defaults them to the values that make a bar-by-bar backtest correct:
`MarketDataFeed.Sip` and `Adjustment.Split`. Both stay overridable.

`Sip` is the consolidated tape across all US exchanges — the prices and volumes a backtest should
simulate against. The free-tier alternative, `Iex`, carries only IEX prints: a fraction of
consolidated volume and thin or missing bars on many names. Defaulting to `Sip` means a free-tier
user gets a loud `RestClientErrorException` (an entitlement error) rather than silently degraded
data; acting on a clear error beats discovering distorted results later. A free-tier user opts into
`Iex` deliberately.

`Split` back-adjusts for stock splits. Raw bars render a split as an overnight price cliff — a
4:1 split is a 75% single-bar drop — which fabricates round trips, trips stops, and corrupts causal
indicators. Split adjustment removes exactly that artifact while leaving prices at their actual
traded levels. Dividend adjustment is left off by default: `All` (total return) shifts historical
absolute price levels, which can interact with fixed-price strategy logic, so it is an opt-in choice
rather than the default.

These defaults deliberately diverge from the existing Yahoo path, which serves raw, unadjusted OHLC
from the v8 `quote` block and therefore carries the split-gap hazard. The Alpaca provider does not
replicate that behaviour for symmetry's sake; its default is the correct series.

## Considered options

- **Default the feed to `Iex`** — works for every account out of the box, including free tier.
  Rejected: it silently serves low-quality data, the opposite of a provider's job, and the
  degradation is easy to miss.
- **Default the adjustment to `Raw`** to match Yahoo — symmetrical, but propagates the split-gap
  hazard that corrupts a bar-by-bar simulation.
- **Default the adjustment to `All`** (total return) — defensible if dividends are considered part
  of strategy return, but it shifts absolute historical price levels and is better left an opt-in.

## Consequences

- A free-tier user must pass `MarketDataFeed.Iex` explicitly and accept the data-quality cost.
- Backtests run through Alpaca and through Yahoo on the same symbol and range will not match
  bar-for-bar: Alpaca is split-adjusted consolidated data, Yahoo is raw IEX-grade data. This is
  expected, not a bug.
