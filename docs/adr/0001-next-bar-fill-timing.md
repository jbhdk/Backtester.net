# Next-bar fill timing

Originally an order emitted while processing bar N was filled against bar N itself (Market at
bar N's open — a price already in the past), giving every strategy a lookahead advantage. We
decided that orders submitted during `OnBar(N)` are instead evaluated against bar N+1 (Market
at N+1's open; Stop/Limit at their trigger if N+1's range crosses it). This trades a little
simplicity for backtests that reflect what a live strategy could actually have traded.

## Consequences

- The engine loop processes orders queued on the previous bar against the new bar *before*
  calling the strategy, then queues new orders.
- Stop/Limit orders therefore rest at least one bar, which dovetails with the resting-order
  model (see ADR 0002).
