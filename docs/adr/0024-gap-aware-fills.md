# Gap-aware fills bounded by the bar open

`FillModel_OHLCHeuristic` originally filled every triggered Stop and Limit at its exact trigger
price, ignoring `bar.Open`. This silently truncated gap losses (a stop that gapped through still
"filled" at the stop) and drove parameter optimization toward degenerate tiny-stop optima, where a
loser could never lose more than its ideal stop distance. We decided a triggered order is
**marketable** and fills no better than the bar's open: below-market triggers (Buy limit, Sell stop)
fill at `min(trigger, open)`, above-market triggers (Sell limit, Buy stop) at `max(trigger, open)`;
Market stays at the open. Slippage still applies on top.

This is realistic in *both* directions rather than conservative: stops now fill worse on a gap
(honest losses), and limits fill *better* when the bar gaps past them (a resting offer into an
opening auction genuinely gets the improved open, so filling at the limit under-credited reality).
The one optimistic assumption is that a limit is filled at the open print — i.e. sufficient
opening-auction liquidity; for a thin instrument gapping on no volume this credits a fill that might
not fully materialize. We accept that over the alternative of systematically under-crediting
favourable gaps.

## Consequences

- Behaviour changes in place — no legacy flag — because the prior rule was a modelling error, not a
  defensible alternative. `IFillModel` remains the seam for anyone who wants different execution
  semantics.
- Every historical backtest number moves. Results that leaned on truncated gap losses (tight-stop
  strategies especially) will report worse, more honest drawdown and expectancy.
- Names the ubiquitous-language term **Gap-aware fill** (pricing), distinct from **Next-bar fill**
  (timing, ADR 0001).
