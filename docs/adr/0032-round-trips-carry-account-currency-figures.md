# Round trips carry account-currency figures, and the Optimizer asks the Portfolio

[ADR 0029](0029-instrument-and-multi-currency-forex-accounting.md) converted the account's aggregates
and left prices native, which is right. What it did not do is finish the job on the figures a
*consumer* derives from a round trip. Three report columns multiply a native price by a quantity and
then divide by, or compare against, an account-currency amount:

- **Margin** — a Reg-T rate times a native entry notional, which also ignores the per-instrument
  `MarginRate` [ADR 0030](0030-forex-margin-via-per-instrument-leverage.md) introduced
- **Leverage** — that same native notional over `EntryEquity`, which is marked equity in the account's
  currency
- **R multiple** — an account-currency `RealizedPnL` over a native `InitialRisk`, and
  `PerformanceCalculator`'s "Avg R" divides the same two

A fourth, and the worst, is not in the report at all: **realized equity** —
`Cash + Σ AveragePrice·Quantity` — adds account-currency cash to a native cost basis, in
`Portfolio.SnapshotAt` and again in `RiskPerTradeSizing`. ADR 0029 converted that sizing model's stop
distance denominator and left its numerator mixing units, so a cross-currency strategy sizes against
an equity figure inflated by roughly the exchange rate. That is wrong trading, not wrong reporting.

## The round trip carries the converted figures

Following [ADR 0023](0023-round-trip-initial-risk-and-r-multiple.md) exactly — the primitive lives on
the domain `RoundTrip`, and consumers only divide:

- **`InitialRisk` becomes Account-currency**, translated at the rate in force when the trip opened. It
  changes denomination in place rather than gaining a converted sibling: two fields both meaning "risk"
  that disagree only for forex is the same landmine ADR 0023 rejected when it refused to let two things
  both called "R" coexist. Single-currency runs are bit-identical.
- **`EntryNotional` is new**: the account-currency capital the trip committed, accumulated per fill from
  the number `Portfolio` already computes to move `Cash`, and reduced pro-rata on a partial exit. A trip
  that scales in across a rate move therefore carries what actually left the account.
- **`EntryMargin` is new**: `Portfolio` applies its own `MarginRate(symbol, side)` — the Instrument's
  rate when it declares one, else the Reg-T split — to that converted notional and stamps the result.
  `MapRoundTrips` loses both rate parameters, and the report stops knowing Reg-T exists.
- **`QuoteCurrency` is new**: the trip states the currency its native `EntryPrice`/`ExitPrice` are in,
  so a mixed report never shows two identically-formatted price columns in different currencies.
- **`Portfolio.RealizedEquity`** joins `MarkedEquity` as a converted property, and both `SnapshotAt` and
  `RiskPerTradeSizing` read it instead of recomputing the sum. This is tracked as its own issue: it
  changes how trades are sized, not how they are displayed.

**Each figure is translated at its own moment.** Risk, notional, and margin at entry; `RealizedPnL` at
exit, exactly as it ships today. So for a JPY-quoted trip in a USD account that risked 100 JPY at
`USD_JPY` 100 and made 200 JPY at `USD_JPY` 125, the report reads: risk **$1.00**, profit **$1.60**,
**1.6R**. It does not read 2.0R, the native price ratio.

## The Optimizer asks the Portfolio for its conversion series

`Optimizer.FetchOnceAsync` pre-fetches only the tradable symbols, so a Trial whose `Portfolio` declares
a `ConversionSymbol` reads an empty series for it and dies with `MissingConversionRateException`.

The Optimizer now calls `portfolioFactory()` once at setup purely to read `ConversionSymbols`, and
pre-fetches those alongside the tradable symbols — the same single hand-off point
[ADR 0031](0031-currency-converter-module.md) gave the Engine, asked the same way. Because a
`Func<Portfolio>` cannot promise every call declares the same Instruments, each Trial's Portfolio is
checked against the pre-fetched set before its Engine runs and throws naming the symbol and the factory,
so the user reads "your factory returned inconsistent Instruments" rather than a missing-rate error from
inside a bar loop. A conversion series resolves its warmup exactly as a tradable symbol does, matching
`Engine.FetchSymbolSeriesAsync`, so a Trial reads precisely the bars an equivalent single run reads.

## Considered options

- **Convert in the report, using the Portfolio's converter.** Rejected twice over: it would translate a
  historical entry at the run's *final* rate, and `PerformanceCalculator`'s "Avg R" — which has no
  report — would stay broken. The defect is in the primitive, not in its display.
- **Stamp the entry rate and let consumers convert.** Rejected: every consumer would then also need the
  `ConversionOperation` to know whether to divide or multiply, spreading the arithmetic ADR 0031 just
  concentrated into one module across two more call sites.
- **Convert `InitialRisk` at the exit rate** so the single rate cancels and R equals the native price
  ratio (2.0R above). Rejected: it reconciles arithmetically, but "initial risk" would then be a number
  that was never true at entry, contradicting ADR 0023's frozen-at-entry semantics and the
  realism-first question of what a broker would have shown you as you entered.
- **Freeze one conversion rate at open** and apply it to the blended entry price. Rejected: for a trip
  that scaled in across a rate move it reports money that never moved.
- **Give the Optimizer its own `Instrument[]` or `conversionSymbols` parameter.** Rejected: it
  re-creates precisely the two-hand-off-points mis-wiring [ADR 0031](0031-currency-converter-module.md)
  deleted from the Engine.
- **Extract a shared run-setup type** used by both Engine and Optimizer (the architecture review's
  candidate 3). Not rejected — deferred. It is the deeper fix, and asking the Portfolio is compatible
  with it landing later.
- **Exempt a conversion series from bar-count warmup** in the Optimizer. Rejected: it would make a
  Trial read different bars than the same run outside the Optimizer. If the rule is wrong it is wrong
  in the Engine, and should be changed there once.

## Consequences

- `RoundTrip.InitialRisk` changes denomination. Any consumer reading it for a cross-currency run was
  reading a mixed-unit number; single-currency runs see no change at all.
- The report's per-trip **Margin** column starts respecting ADR 0030, so a 50:1 forex instrument stops
  reporting 50% margin.
- `Position` gains running account-currency cost-basis state, maintained on every fill and reduced
  pro-rata on partial exits.
- An Optimization over a cross-currency Portfolio works. A `portfolioFactory` that returns *varying*
  Instruments is now refused rather than silently under-fetched.
- `ReportModelBuilder.MapRoundTrips` no longer takes margin rates — a signature change to a public
  report seam.
