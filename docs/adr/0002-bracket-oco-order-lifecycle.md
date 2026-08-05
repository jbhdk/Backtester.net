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
Bracket must have **at least one** leg; a zero-leg request throws `ArgumentException` (caller misuse,
distinct from the funds rejection that returns null — an entry with no protection is a plain
`Submit`). The glossary's `Bracket` and `OCO` entries are updated to match.

*(The throw moved from `SubmitBracket` to `BracketRequest`'s constructor when the request was
reshaped; see the ADR 0025 amendment of 2026-08. The rule is unchanged.)*

## Amendment: several Brackets may be live on one symbol (2026-08)

A symbol may carry **more than one live Bracket at a time**, so a strategy can scale into a position
with a second bracketed entry while the first still rests. Each Bracket owns its own legs and its own
OCO group: a leg fill cancels only *its* sibling and retires only *its* Bracket, leaving every other
Bracket on the symbol guarding what remains of the position. The alternative — rejecting a bracketed
entry on a symbol that already has one live — was rejected: it forbids scaling in, and a live broker
does not refuse a second bracket either.

The rule this imposes on the exit path: **any** fill that flattens the position closes what *every*
Bracket on the symbol was guarding, so it cancels the resting legs of **all** of them. Cancelling only
the most recently armed Bracket left the earlier one's stop and target resting on from flat, where a
later bar could fill one and open a phantom Position (#132).

Note that "any fill" now includes a protective **leg** fill. With one Bracket per symbol a leg fill
needed no cancel — the OCO group took the sibling and nothing else rested — so the check was scoped to
non-leg fills. With several Brackets live, a leg closing the last of the position leaves the *other*
Brackets' legs resting against flat, which is the same phantom-Position hazard by a different route.
A leg fill needs no special case in the cancel itself: its own Bracket has already retired and
released its legs by then.

This says nothing about leg quantities: each Bracket's legs cover the size *its own* entry filled, so
a leg fill closes that much of the position and leaves the rest under the other Brackets' protection.
Pairing such partial exits into **Round trips** is the Portfolio's existing concern, unchanged here.
