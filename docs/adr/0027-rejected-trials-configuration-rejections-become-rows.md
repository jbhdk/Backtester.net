# Rejected trials: configuration rejections become rows

`TrailingStopManager` rejects an inverted trail configuration — a maximum trail distance below the
minimum — at construction, because the interpolation would otherwise *loosen* the stop as profit grows,
the opposite of the documented ratchet. Managers are constructed per-trade inside a strategy's `OnBar`,
so during an Optimization such a rejection fires mid-Trial, deep inside the parallel sweep. Before this
decision the exception propagated out of `RunAsync` and destroyed every completed Trial's work: one
invalid Parameter set killed the whole sweep with nothing written.

The Optimizer now captures a **configuration rejection** — an argument rejection thrown while building
the Trial's strategy and broker or while running its backtest — and records the Trial as a **Rejected
trial**: no Performance stats, no Score, never eligible, never Best, ranked below every scored Trial,
and shown on the leaderboard as a full row carrying its Parameter set and the rejection's reason.
Deliberately narrow: **only** configuration rejections are contained. Any other exception — an engine
defect, a data fault — still propagates and stops the sweep, so genuine bugs stay loud; cancellation
propagates untouched.

The alternative was a declared constraint seam (`.Where(parameters => ...)`) pruning invalid sets from
the Parameter space before expansion, so no compute is spent on them. Deferred, consistent with ADR
0020's stance of not adding a seam speculatively: rejection keeps every set visible — mirroring
**Eligibility**'s shown-never-hidden stance — whereas pruning would silently shrink the "cartesian
product" meaning of the Parameter space. The seam can still be added later if sweeps routinely carry
large known-invalid regions.

## Consequences

- `Trial` carries a rejection state (the reason, with stats/score absent); consumers must check it
  before reading stats. The leaderboard renders Rejected trials as flagged rows at the bottom.
- A sweep whose ranges overlap an invariant burns a partial backtest per rejected set on every run.
  That waste is visible on the leaderboard by design — the prompt to tighten the ranges lies with the
  caller.
- An argument rejection thrown by a genuine engine bug inside a Trial is misfiled as a Rejected trial
  rather than stopping the sweep. Accepted: the reason text is displayed on the row, not swallowed, so
  the defect is still surfaced.
