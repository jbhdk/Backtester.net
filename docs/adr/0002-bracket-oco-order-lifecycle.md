# Broker-owned order lifecycle: resting orders, Cancel/Modify, and Bracket + OCO

A strategy needs an initial stop-loss, a take-profit, and a trailing stop on every position.
With single-shot fills there was no way to do this safely: submitting a stop and a target that
both fall within one bar's range filled *both*, over-selling the position. We decided the broker
owns order lifecycle — orders rest (GTC) until filled or cancelled, support `Cancel` and
`Modify`, and a full **Bracket** object (entry + attached stop + target) is a first-class
broker concept whose stop and target form an **OCO group enforced inside the broker**: when one
leg fills, the broker auto-cancels the sibling. Trailing is the strategy calling `Modify` on the
stop each bar (it knows the current ATR).

## Considered options

- *Strategy-managed day-orders* (re-emit exits each bar, at most one per bar): rejected — pushes
  fragile OCO/precedence logic into every strategy.
- *Resting orders + cancel but no bracket* (strategy wires OCO on fill events): rejected — leaves
  OCO correctness to each consumer.

## Consequences

- The strategy submits exit intent once; the broker guarantees the stop and target never both
  fill. This is the headline reason the engine can express realistic strategies.
