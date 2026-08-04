# Instrument and multi-currency forex accounting

> **Amended by [ADR 0031](0031-currency-converter-module.md).** Two clauses below are superseded: the
> `Engine` constructors taking `Instrument[]` (Instruments now reach a run through the Portfolio
> alone, which owns the Currency converter and declares the conversion series to fetch), and the
> silent native-amount fallback this design shipped with (a declared conversion with no observed rate
> now throws). The rest of this ADR stands — including its rejection of engine-side symbol inference,
> which ADR 0031 upholds.

Data.Oanda is the engine's first forex provider, and forex breaks an assumption baked into every
existing consumer: that a symbol's price is already denominated in the account's own currency.
`EUR_USD` prices in USD, but `USD_JPY` prices in JPY — a USD account holding `USD_JPY` needs its
JPY-denominated PnL converted to USD, using an exchange rate that moves over the backtest's date
range like any other price series.

`Portfolio(decimal startingCash, string accountCurrency = "USD")` defaults the new parameter to
`"USD"`, so every existing construction — already implicitly USD — needs no change; only a caller
opening a non-USD account passes it explicitly.

We introduce `Instrument`, a caller-supplied per-symbol metadata record carrying `Symbol`,
`QuoteCurrency`, and (when `QuoteCurrency` differs from the account's currency) a `ConversionSymbol`
naming the exact series to fetch for converting it — e.g. an Instrument quoted in JPY names
`USD_JPY` when the account is USD. `Portfolio` gains an `AccountCurrency` property as the single
source of truth its cash and equity are denominated in.

`Engine`'s canonical constructors take `Instrument[]`: a stock/ETF Instrument simply sets
`QuoteCurrency` equal to the account's currency and leaves `ConversionSymbol` null, so no fetch and no
conversion happen. A `string[] symbols` overload stays alongside it as a thin convenience: it wraps
each ticker into a trivial Instrument (`QuoteCurrency = AccountCurrency`, no conversion, default Reg-T
margin) and delegates to the Instrument-based constructor underneath — one implementation, not two
paths to keep in sync. Existing Yahoo/Alpaca call sites, the whole `EngineTests` suite, and
`Optimizer`'s mirrored constructors need no changes at all; only forex/mixed-currency callers ever
construct `Instrument` directly. When an Instrument does declare a
`ConversionSymbol`, Engine fetches it through the same `IHistoricalDataFetcher` seam already used for
every tradable symbol — no new network dependency, just another entry in the fetch set. Its bars are
tracked internally by Portfolio/Engine only: `Engine.RunOnce` never calls `strategy.OnBar` for a
Conversion symbol, and it never appears in the report's symbol list or round trips. It is plumbing a
strategy trading `EUR_USD` in a JPY account never needs to know exists.

Conversion applies only to the currency-denominated aggregates a report expresses in the account's
own currency: `Cash`, `RealizedPnL`, `MarkedEquity`, and isolated equity. `Position.AveragePrice` and
`RoundTrip.EntryPrice`/`ExitPrice` stay in the instrument's native quote currency — the real, checkable
price the pair actually traded at — matching how a live trading platform shows native price alongside
account-currency PnL.

## Considered options

- **Engine computes the conversion symbol algorithmically** from currency codes (e.g. Oanda-style
  string concatenation). Rejected: it bakes one provider's symbol-naming convention into Core/Engine,
  which is deliberately provider-agnostic today (Yahoo, Alpaca, and Oanda all share one Engine).
- **Instrument optional, `string[] symbols` stays Engine's primary API**, with a separate independent
  implementation for forex callers. Rejected: two parallel code paths to keep in sync. The
  `string[]`-overload-as-sugar we adopted instead avoids this — it's one Instrument-based
  implementation with a convenience wrapper on top, not a second path.
- **Convert everything at the fill**, storing account-currency-converted values throughout Position
  and RoundTrip. Rejected: a report could no longer show "USD_JPY traded at 148.523" — only its
  USD-converted equivalent, which is not a real market price anyone could check.
- **A Lot/contract-size concept** for configuring position size in standard/mini/micro lots. Rejected:
  Oanda's own v20 API already trades in raw base-currency units with no lot concept, matching how
  Position.Quantity (`int`) already works for stocks/ETFs; a caller who thinks in lots multiplies
  before configuring a sizing model.

## Consequences

- Existing Engine callers — `EngineTests`, `Optimizer`, `AnalysisSample` — are unaffected: the
  `string[] symbols` convenience overload keeps them compiling and behaving identically. Only forex or
  mixed-currency callers construct `Instrument` directly.
- Sizing models that divide an equity-denominated numerator by a quote-currency-denominated stop
  distance (`RiskPerTradeSizing`, `FixedRiskSizing`) must convert the stop distance through the same
  rate before dividing — a direct correctness consequence of this decision, not a separate design
  question.
- Commission models are unaffected: they already operate on account-currency cash/notional and were
  never quote-currency-denominated.
- Overnight swap/rollover cost and spread-as-cost (per-instrument spread/commission) are explicitly
  deferred to a future round — Instrument is deliberately left room to carry that later, but carries
  only currency information today.
