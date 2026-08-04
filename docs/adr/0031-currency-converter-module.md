# Currency converter module

Amends [ADR 0029](0029-instrument-and-multi-currency-forex-accounting.md), which introduced
`Instrument` and multi-currency accounting but left the conversion rate itself as five implicit
mechanisms with no locality: a silently-extended fetch set, a slice join, the order of two statements
in the engine loop, a side-effect dictionary write, and a conversion method that returned the *native,
unconverted* amount whenever any link in that chain was mis-wired. A run configured slightly wrong
produced JPY numbers presented as USD and said nothing. We concentrate all of it into one module —
the **Currency converter** — owned by the Portfolio.

`CurrencyConverter` is a **concrete class in the engine's core with no C# interface**. There is one
implementation and no second one in prospect, so an interface would be a hypothetical seam; the class
itself is the test surface. Its whole public surface is four members:

```
CurrencyConverter(accountCurrency, instruments)   // holds the declarations, cross-checks them
ConversionSymbols                                  // the extra series a run must fetch
ObserveRate(conversionSymbol, close)               // the feed primitive; also the test seam
ToAccountCurrency(symbol, nativeAmount)            // identity / divide / multiply; throws on missing rate
```

`ObserveRate` is public deliberately: it is how bars reach the module, and the codebase forbids
`InternalsVisibleTo`, so a test that sets a rate needs a public way in. It exposes no more mutable
state than Portfolio already does.

**The construction cross-check makes the quote currency declaration load-bearing.** `QuoteCurrency` is
required on every Instrument; one differing from the account currency requires a `ConversionSymbol`,
and one equal to it forbids one. Each violation throws `InstrumentDeclarationException` naming the
symbol, the moment the Portfolio is built. Currencies compare case-insensitively, so an ISO code's
casing never decides the check. Before this, the quote currency was read by nothing and a forgotten
Conversion symbol was undetectable.

**A missing rate fails loudly.** Applying a declared conversion before any rate has been observed
throws `MissingConversionRateException`, naming both the symbol and its Conversion symbol. The silent
native-amount fallback is gone. Identity conversion — an Instrument declaring no Conversion symbol —
is unchanged and never throws, even before any bar has printed. A mid-run gap in the conversion series
still carries the last observed rate forward: a quiet bar is not a mis-wiring and must not fail a
healthy run.

**`ConversionOperation` gives both rate directions**, declared on the Instrument beside its Conversion
symbol. A conversion pair whose *first* currency is the account currency divides (`USD_JPY` in a USD
account — JPY per USD); one whose first currency is the *quote* currency multiplies (`GBP_USD` for a
GBP-quoted cross such as `EUR_GBP`, where no account-first pair exists). `Divide` is the default and is
exactly the previous behaviour, so declarations written under ADR 0029 keep their meaning. The
converter holds the Conversion symbol and the operation as a single declaration record, so the two
cannot drift apart.

**The Portfolio is the single hand-off point for Instruments.** It builds and owns the converter and
exposes the conversion series a run must fetch; `Engine`'s `Instrument[]` constructor overloads are
deleted (halving its constructor count to four) and it derives its fetch set from the tradable symbols
plus the Portfolio's declared conversion series. An Engine/Portfolio disagreement about which symbols
convert through what is now unrepresentable rather than merely checked — the very mis-wiring the
silent fallback used to hide. A Portfolio declaring no conversion yields the caller's own symbol array
untouched, so a stock/ETF run builds no conversion machinery at all.
`Portfolio.ToAccountCurrency(symbol, nativeAmount)` keeps its exact signature as a one-line delegation,
preserving the sizing-model seam ADR 0029 established; `RiskPerTradeSizing` and `FixedRiskSizing` are
untouched. The dictionary that served both position marking and currency conversion is split: the
Portfolio keeps last closes for marking, the converter owns rate state, and the equity-snapshot
recording feeds each its own.

**The fill-timing invariant is declared on the converter and pinned by tests.** A fill translates at
the conversion pair's *last completed close* — never a rate that was not yet knowable while the fill's
own bar was trading — while an end-of-bar equity mark translates at the pair's *current* close, the
freshest known rate. The rule itself is unchanged; what changes is that it is stated on the module
whose rate state carries it, along with the caller obligation that upholds it (apply a bar's fills
before observing that same bar's Conversion-symbol close), instead of existing only as the order of
two statements in the engine loop where a refactor could silently flip it into currency lookahead.
Translation moves money, never execution semantics: a fill price stays the gap-aware price in the
Instrument's own quote currency ([ADR 0024](0024-gap-aware-fills.md)), whatever the rate does.

**The Instrument factory lives in the provider package.** `OandaInstrument.For(symbol,
accountCurrency)` infers the quote currency, the Conversion symbol, and the Conversion operation from
Oanda's underscore pair naming and its pair-ordering convention, so a caller never hand-writes currency
metadata and cannot pick a direction that contradicts the symbol it named — one ordering decision sets
both. A symbol that is not two ISO codes joined by an underscore is rejected at declaration time. Which
way Oanda names a pair is market convention, not something derivable from the codes, so it is encoded
as a precedence table with `JPY` held out as Oanda's universal quote currency (ranked below even an
unrecognized currency, since `SGD_JPY`, `TRY_JPY` and `ZAR_JPY` all exist). All of it stays in
`Data.Oanda`: nothing in the engine core parses a symbol, exactly as ADR 0029 decided.

## What this supersedes in ADR 0029

- **Engine constructors taking Instruments.** ADR 0029's "`Engine`'s canonical constructors take
  `Instrument[]`… a `string[] symbols` overload stays alongside it as a thin convenience" is
  superseded: `Engine` now takes only the tradable symbol list, and Instruments reach the run through
  the Portfolio alone. Its stated *consequence* — that existing symbol-list callers are unaffected —
  still holds, and for the same reason.
- **The silent native-amount fallback.** ADR 0029's design shipped with a conversion that returned the
  unconverted native amount when no rate had been observed. It is replaced by the fail-loud policy
  above.

Everything else in ADR 0029 stands, including its rejection of engine-side symbol inference, which
this design upholds: conversion still applies only to the account-currency aggregates (`Cash`,
`RealizedPnL`, `MarkedEquity`, isolated equity) while `Position.AveragePrice` and
`RoundTrip.EntryPrice`/`ExitPrice` stay native, and `Instrument` still carries only currency and margin
metadata. [ADR 0030](0030-forex-margin-via-per-instrument-leverage.md) is untouched.

## Considered options

- **An `ICurrencyConverter` interface with the Portfolio depending on the abstraction.** Rejected: one
  implementation, and no plausible second — a pluggable conversion *policy* is not a thing a backtest
  needs, since a live broker converts one way. The seam would exist only to be mocked, and the
  codebase's own rule forbids mocking solution types anyway.
- **Keep the silent native-amount fallback, or convert at a default rate of 1.** Rejected on the
  codebase's reject-rather-than-clamp stance: both present native-currency numbers as account currency,
  which is the exact failure this module exists to remove.
- **Infer the rate direction by parsing the Conversion symbol** (split it on the separator, compare the
  first code to the account currency) instead of declaring `ConversionOperation`. Rejected: that is
  engine-side symbol parsing under another name, which ADR 0029 rejected and this ADR upholds. Rate
  direction is *declared*; the provider's Instrument factory is what fills the declaration in.
- **Keep `Engine`'s `Instrument[]` overloads and validate that Engine and Portfolio agree.** Rejected:
  a check that two hand-offs match is strictly weaker than having one hand-off. The removal also halves
  a constructor count that ADR 0029 had doubled.
- **Intra-bar rate precision** — translating a fill at the conversion bar's *open* rather than the
  previous close. Rejected: a negligible realism gain for extra per-bar rate state, on a rate that
  moves far less within a bar than the traded price does. It stays revisitable, and the converter's
  remarks name it as the one place it would be revised — which is the locality this module buys.
- **An Instrument factory in the engine core**, keyed by provider name. Rejected for ADR 0029's own
  reason: it bakes one provider's symbol-naming convention into a deliberately provider-agnostic core.
  Yahoo and Alpaca get their own factories when they first serve forex.
- **`ObserveRate` internal, with `InternalsVisibleTo` for tests.** Rejected: the codebase tests through
  public APIs and does not widen visibility for tests.

## Consequences

- Every `Instrument` must now declare a `QuoteCurrency`, including one that exists only to set a
  `MarginRate`. This is a construction-time break, reported by symbol, not a silent behaviour change.
- A cross-currency run that would previously have produced quietly wrong numbers now throws — at
  Portfolio construction for a contradictory declaration, at the first conversion for a conversion
  series that never printed.
- Callers that constructed `Engine` with an `Instrument[]` pass the array to `Portfolio` instead. Plain
  symbol-list runs — the whole existing stock/ETF surface — are unchanged and allocate no conversion
  machinery.
- A currency test sets a rate with one `ObserveRate` call instead of assembling four collaborators and
  a hand-built market slice. The end-to-end path (equity snapshot feeds the converter) keeps a small
  number of tests on it deliberately, so the feed itself stays pinned.
- A currency bug now has exactly one place to be: the missing-rate policy, the conversion arithmetic,
  and the declaration rules are all in `CurrencyConverter`.
- `Optimizer` remains symbol-list-only, so an optimization of a forex strategy still runs without
  conversion. That is a real defect and deliberately out of scope here — it belongs with shared run
  setup, not with this module.
- Overnight swap/rollover, per-instrument spread/commission, and maintenance margin remain deferred as
  ADR 0029 and ADR 0030 left them.
