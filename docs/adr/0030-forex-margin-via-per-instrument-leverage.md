# Forex margin via per-instrument leverage

Narrows [ADR 0011](0011-margin-account-shorting.md), which made Reg-T initial margin (50% long / 150%
short, account-wide, hardcoded) the account's sole margin gate and explicitly "not a pluggable
execution model." That stance fit its context — U.S. equities, where the long/short asymmetry is a
real regulatory rule — but forex margin doesn't work that way: a broker quotes one leverage ratio per
instrument (e.g. 50:1 major pairs = 2%, lower leverage for minors/exotics) applied symmetrically to
both longs and shorts.

`Instrument` gains a nullable `MarginRate`. When set, Portfolio's margin gate uses it for both long
and short on that symbol; when unset, Portfolio falls back to its existing
`LongInitialMarginRate`/`ShortInitialMarginRate` Reg-T split, so every stock/ETF Instrument that
doesn't set a `MarginRate` behaves exactly as before. The gate itself stays intrinsic and always
enforced — ADR 0011's core principle, that margin can never be silently switched off, is unchanged.
Only the *rate* now varies per instrument instead of being one hardcoded account-wide asymmetric pair.

## Considered options

- **One account-wide symmetric rate replacing Reg-T entirely.** Rejected: a mixed portfolio (stocks
  alongside forex, or forex majors alongside exotics) needs different leverage per instrument, not one
  rate for the whole account.
- **Keep Reg-T only, approximate forex leverage via the existing long/short split.** Rejected: Reg-T's
  50%/150% asymmetry is a U.S. equities rule with no forex meaning, and can't represent a symmetric
  ratio like 50:1 (2% both directions).

## Consequences

- A mixed stock+forex account, or a forex account trading both majors and exotics, sizes margin
  correctly per instrument rather than per account.
- ADR 0011's "not a pluggable execution model" language is clarified to mean the gate is never
  disabled, not that its rate can never vary by instrument.
