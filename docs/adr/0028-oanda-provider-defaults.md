# Oanda provider defaults to Mid price, Practice environment, tick-count volume

`OandaHistoricalDataProvider` exposes the price component and environment as constructor
parameters, defaulting to `PriceComponent.Mid` and `OandaEnvironment.Practice`. Both stay
overridable. `Candle.Volume` is always populated from Oanda's per-candle tick count, with no
override — forex spot has no consolidated traded-volume figure to opt into instead. This follows
the same shape of decision [ADR 0010](0010-alpaca-correct-data-defaults.md) records for Alpaca's
feed and adjustment defaults: each default is hard to reverse once consumers depend on it,
non-obvious without the reasoning, and trades off real alternatives.

`Mid` averages bid and ask into a single price series, comparable to the single-price series
equities providers (Yahoo, Alpaca) already produce and matching how most retail forex backtests are
built. `Bid`/`Ask` remain available for anyone specifically modeling one side of the spread — for
example, testing entry/exit logic against the price a live order would actually fill at.

`Practice` targets Oanda's free demo host (`api-fxpractice.oanda.com`). It requires no funding or
live-trading approval to obtain an API token, and Oanda serves identical real market candles to
`Live` — this is not a data-quality trade-off, only an accessibility one. Users backtesting against
their own live account's price stream opt into `Live` explicitly.

`Volume` carries Oanda's per-candle tick count (the `volume` field in the v20 candles response), not
a consolidated traded-volume figure. Forex spot trading is decentralized across liquidity providers,
so no single "shares traded" number exists the way it does for equities. Tick count is passed
through as a documented activity/liquidity proxy rather than zeroed out, preserving information a
strategy might use (e.g. filtering low-liquidity bars) instead of discarding it silently.

## Considered options

- **Default the price component to `Bid` or `Ask`** — realistic for modeling a specific order side,
  but skews the default series toward one side of the spread and diverges from how equities
  providers report a single price. Rejected as the default; kept available as an override.
- **Default the environment to `Live`** — signals "real account," but gates the default path behind
  funding or live-trading approval for no data-quality benefit, since `Practice` serves the same
  market candles. Rejected: raises the bar to get started without buying anything in return.
- **Zero out `Volume`** to signal "no true volume exists," matching how some equities feeds handle
  missing data. Rejected: it discards a real, documented Oanda field (tick count) that carries
  genuine activity information, in favor of a technically-honest but strictly less useful value.

## Consequences

- A user who wants bid- or ask-side candles must pass `PriceComponent.Bid`/`PriceComponent.Ask`
  explicitly; the default `Mid` series is a synthetic average, not a tradeable quote.
- A user backtesting against their live account's price stream must pass
  `OandaEnvironment.Live` explicitly.
- `Candle.Volume` from Oanda is not comparable to `Candle.Volume` from an equities provider — it is
  a tick count, not shares or contracts traded. Cross-provider volume comparisons or strategies that
  assume a traded-volume figure are not meaningful when Oanda is the source.
