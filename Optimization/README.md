# backtester.net.optimization

Parameter optimization for the [backtester.net](https://www.nuget.org/packages/backtester.net) engine.

An **Optimizer** sweeps a strategy's **Parameters** over a grid (each Parameter given a range and step),
runs a backtest per combination, and ranks the resulting **Trials** by an **Objective**. The produced
**Optimization** carries every Trial's performance and score plus the best one.

## Scope

v1 is **in-sample grid search** (ADR 0020): the Optimizer tunes and reports on the same data, with no
out-of-sample or walk-forward validation. A winning Trial is the best *in-sample* configuration, not a
validated one. Overfitting is guarded only by a minimum-trades **Eligibility** rule and by surfacing the
full ranked leaderboard.
