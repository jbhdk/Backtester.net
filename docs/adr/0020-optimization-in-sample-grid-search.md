# Optimization is in-sample grid search

The Optimizer tunes and reports Trials on the **same** data: v1 has no out-of-sample or walk-forward
split, so a winning Trial is the best *in-sample* configuration, not a validated one. Overfitting is
guarded only by the minimum-trades **Eligibility** rule (a Trial with too few round trips cannot win)
and by surfacing the **full ranked leaderboard** — so a winner's neighbours reveal whether the peak is
a plateau or a lucky spike — rather than by a train/test split. We accept this because the value of v1
is fast parameter comparison with honest transparency, not statistical validation.

The search is an exhaustive grid over the Parameter space with **no `ISearchStrategy` seam** — following
ADR 0005's stance of introducing an abstraction deliberately when a second implementation exists, not
speculatively. Walk-forward / holdout validation and adaptive search (random, Bayesian) are deferred
behind that future seam.

## Consequences

- A reader must treat the reported winner as the best in-sample fit, never as evidence the strategy
  will hold up live. The package README and report should say so plainly.
- The Optimizer re-runs the existing `Engine` unchanged, once per Trial, over bars **fetched once** and
  shared across Trials via an in-memory `IHistoricalDataFetcher` (the seam ADR 0005 sanctions for
  in-memory consumers) — so every Trial provably sees identical data and the engine gains no
  optimization-specific entry point.
