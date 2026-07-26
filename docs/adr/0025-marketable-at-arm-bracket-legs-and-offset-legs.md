# Marketable-at-arm bracket legs fill on the arming bar; fill-relative offset legs

A bracketed entry's absolute protective stop could rest on the **wrong side of the actual entry
fill** and detonate the reported R-multiple (a −28R short-SPY round trip: entry 379.746, stop 379.642,
exit 382.725). A strategy computes an absolute stop from a *pre-fill* reference (the signal bar's
close), the market entry fills a bar later through a gap, and by fill time the absolute price is on the
non-protective side. Issue #99 asked for two engine features to fix this: fill-relative bracket legs,
and a fill-time protective-leg *invariant* with a four-way `BracketInvariantPolicy` enum
(`Throw`/`Reposition`/`Reject`/`Warn`). We shipped the first and **deliberately did not build the
second** — the invariant misdiagnosed the failure.

## What we decided

1. **Fill-relative offset legs (prevention).** `BracketRequest` gains nullable `StopOffset` and
   `TargetOffset` — a per-share distance the engine resolves against the **slippage-adjusted** entry
   fill once it fills: long `stop = fill − stopOffset`, short `stop = fill + stopOffset` (target
   mirrored). Anchoring to the recorded fill makes the realized **Stop distance** — and therefore
   **Initial risk** and R (ADR 0023) — equal the requested offset *exactly*, regardless of any gap
   between decision and fill. The resolved price is what is stamped as the entry's `EntryStopPrice`, so
   R is derived from the guaranteed distance, not the stale absolute anchor.

2. **Marketable-at-arm legs fill on the arming bar (honest execution).** A protective leg is armed by
   its entry's fill. If that leg is **already marketable at the arming bar's open** — the fill gapped
   through the leg's price — it fills on that *same* bar at the gap-aware price (ADR 0024), rather than
   resting to the next bar. This is symmetric (stop or target; at most one can be through the market,
   since they straddle the fill) and it produces a zero-bar, near-scratch round trip. It mirrors a live
   bracket: the parent fills and an already-triggered child executes right after.

## Why not the proposed invariant + policy enum

Following the realism model to its conclusion showed the −28R is **not a validation failure — it is a
timing artifact**. A live broker fills the entry and triggers the already-through stop *immediately*,
so you cover at ~your entry for a scratch. The engine's next-bar rule (ADR 0001) instead made the
breached stop rest a full bar, during which the adverse gap kept running and *manufactured* the loss.
Re-priced with same-bar execution, the evidence round trip covers at 379.746 (its own entry) for
~0 loss and R ≈ 0 — no detonation, nothing to police.

- **`Throw`** would abort a run (and, worse, a whole Optimization sweep, ADR 0020) over a market
  outcome a real broker executes without complaint. The right way to *avoid* the outcome is an offset
  leg, not a crash.
- **`Reposition` (clamp to the protective side)** cannot know the distance the strategy intended —
  that information exists only in the offset form. It could only clamp to `fill ± epsilon`, driving
  Initial risk toward zero and detonating R in the *other* direction. It is a half-baked reimplementation
  of offset legs. (It also conflicts with the standing "reject, don't clamp" preference.)
- **`Reject`/`Warn`** exist only to make a broken absolute stop survivable, which same-bar execution
  already does correctly — the round trip completes as a benign scratch.

## Consequences

- **Execution changes in place — no legacy flag** (same stance as ADR 0024). Every backtest with a
  bracket leg gapped-through at entry re-prices to a smaller, honest loss; results that leaned on the
  one-bar-delayed blow-up move.
- A scoped exception to the next-bar rule (ADR 0001): a leg *already marketable the instant it is
  armed* fills on the arming bar (not lookahead — it is marketable now); a leg the arming bar merely
  trades *through* later keeps ordinary next-bar timing, so the OCO "stop and target both fill in one
  bar" hazard (ADR 0002) is not reopened.
- No report change: a same-bar breached-stop round trip is already a legible full row — **bars held 0**,
  exit reason **Stop-loss**, **R ≈ 0**. No new field or run-level count.
- Submit-time validation on `BracketRequest`: setting both the absolute and offset form for one leg,
  a non-positive offset, or a zero-leg request (neither form on either leg) all throw `ArgumentException`
  — caller misuse, distinct from the funds rejection that returns null (ADR 0002 amendment).
- No `BracketInvariantPolicy`, no configuration knob, no fill-time invariant type is introduced.
