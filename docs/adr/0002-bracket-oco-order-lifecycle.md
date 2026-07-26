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

## Amendment: single-leg brackets (2026-07)

`BracketRequest.StopPrice` and `TargetPrice` are now nullable: a Bracket may attach a stop-loss
**and/or** a take-profit. The **OCO group is conditional on two legs** — with a single leg there is
no sibling to cancel, so the lone leg simply rests until it fills or a Signal exit cancels it. A
Bracket must have **at least one** leg; `SubmitBracket` throws `ArgumentException` on a zero-leg
request (caller misuse, distinct from the funds rejection that returns null — an entry with no
protection is a plain `Submit`). The glossary's `Bracket` and `OCO` entries are updated to match.
