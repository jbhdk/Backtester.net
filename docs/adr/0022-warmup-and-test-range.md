# Warmup and the Test range

A run now takes two windows instead of one: a **Test range** — the bars it steps through — and a
wider **Data range** that adds an optional **Warmup** lead-in ahead of it. The loop, all accounting,
and the report follow the Test range; the Warmup bars reach the strategy's `OnStart` History only, so
a strategy's indicators are already valid on the first Test bar without the lead-in ever being traded
or measured. This lets a run be tested over a period distinct from the data it is handed — the
in-sample / out-of-sample and walk-forward workflow the Coverage floor and `Prime` (ADR 0021) opened
up at the Cache level, now expressed within a single run.

## Why two windows, and why the loop follows the *Test* range

Before this, `Engine` took one `from`/`to` that governed both what was fetched and what was iterated,
so "fetched" and "measured" were the same span and warmup was impossible: the loop began measuring at
the first fetched bar. Splitting the windows is the minimum needed to warm an indicator without
polluting the results.

We considered two ways to split. The one **rejected** was: loop over the whole Data range (warmup
included) and then *clip* every time-based statistic back to the Test range. It works, but it forces a
"measurement window" boundary through `PerformanceCalculator`, the equity curve, drawdown, and the
round-trip set, and it makes "which stats respect the window" a per-stat question. The one **chosen**
loops only the Test range: because equity snapshots and round trips are produced *inside* the loop,
every statistic — time-based and trade-based alike — is confined to the Test range **by
construction**, with no clipping code. `OnStart` still receives the full Data-range History, so
precompute is warm. The single thing given up is that `OnBar` does not fire during Warmup; a strategy
that warms state incrementally in `OnBar` rather than in `OnStart` would not be warmed. Every strategy
here precomputes in `OnStart`, so this costs nothing today, and it is the reason the choice is
recorded rather than assumed.

## Decision detail

- The **Data range's end equals the Test range's end**; only its start may reach further back. Extending
  the Data range *past* the Test end is the one move that would feed `OnStart` post-Test bars and
  reintroduce lookahead, so it is deliberately not offered — Warmup only deepens the front.
- **Warmup is optional and caller-chosen in three forms**, as constructor overloads on `Engine` and
  `Optimizer`; absent, the Data range equals the Test range. A `TimeSpan` subtracts from the Test start;
  a `DateTime` is an absolute Data start (guarded `≤` the Test start); an `int` is a **per-symbol** bar
  count — "N bars before the Test start" resolves to a different calendar date per symbol, is resolved at
  fetch time from the bars themselves, and a symbol without N bars available above its Coverage floor is
  **refused**, mirroring `DataCoverageException` (ADR 0021) rather than served short. The three forms are
  fronted by overloads but backed by an internal polymorphic `Warmup` value object, so a future form is a
  subclass, not a `switch`.
- **The report is clipped to the Test range.** `BacktestResult`'s `from`/`to`, its candle series, and the
  strategy's exposed indicator series are all trimmed to the Test range, so the chart aligns with the
  equity curve and no indicator point lands at a timestamp the candles lack (the off-candle whitespace
  hazard). Warmth is not lost: an indicator's *values* were computed over the full Data-range History, so
  its line is already at the right level on the first drawn bar — only the lead-in is not *drawn*.
- **The Optimizer mirrors the Engine.** It fetches once over the Data range (so the shared in-memory bars
  include the Warmup), then runs every Trial over one shared Test range and one shared Warmup. Warmup is
  **fixed run configuration, never a swept Parameter** — it concerns data sufficiency, not strategy
  behaviour, and holding it identical across Trials is what keeps their Scores comparable.
- **`Prime` remains the across-runs complement (ADR 0021).** The wide block for a multi-period study is
  warmed into the Cache once by a `Prime`; each run's Data range then stays small — its Test range plus
  the Warmup it needs — and is served from the Cache. A run's Data range is its own reach into the Cache,
  not the whole cached block.

## Consequences

- `Engine` and `Optimizer` gain Test-range + Warmup overloads; `fromUtc`/`toUtc` become the Test range.
  Existing callers move mechanically (rename to the Test range, no Warmup), and `BacktestResult`'s
  `from`/`to` keep meaning "the period the report shows."
- A too-*short* Warmup silently under-warms an indicator for the period and date forms (visible in the
  chart); a too-*long* one is harmless, since Warmup bars are never looped or measured. Only the bar-count
  form refuses rather than under-warms.
- Ubiquitous language added in `CONTEXT.md` under **Run windows**: Data range, Test range, Warmup.
