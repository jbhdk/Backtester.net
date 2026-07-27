# A reducing order is sized by the position it closes, not the sizing model

A stop-and-reverse strategy flattens then re-enters by submitting two quantity-less market orders — one
to close the open position, one to open the opposite side — and leans on the sizing model to fill in
each quantity (see `MovingAverageCrossStrategy`). With a fixed-size model that works by coincidence: the
close is sized to the same constant the position was opened at, so it closes exactly. With
`RiskPerTradeSizing` it breaks two ways: the flatten market order carries no stop, so the risk model
sizes it to zero and the broker drops it — the old position is never closed before the reversed entry
arms; and even a model that returned a number would return the *wrong* one, because a close's size is
set by the position it closes, not by a risk budget. And the strategy cannot work around it: `SubmitOrder`
unconditionally overwrites `request.Quantity` with the sizer's output, so a hand-sized flatten is ignored.

We decided that **the sizing model sizes only opening orders; a reducing order is sized by the position
it closes**. In `SubmitOrder`, when the order opposes the open position (`Portfolio.ReducesOpenPosition`):

- a **quantity-less** reducing order flattens the whole position (`|Portfolio.OpenQuantity|`);
- an **explicit** reducing quantity performs a partial reduce and is respected as given;
- overshoot stays clamped at fill (ADR is unchanged there), so neither can flip the position's sign.

Opening and same-direction adds are sized by the model exactly as before. This mirrors the margin gate,
which already treats a reducing order specially (it commits no initial margin), and it restores the
domain statement that *you never risk-size a close*.

## Considered options

- *Reducing orders bypass the sizer (chosen)* vs. *let any explicit quantity bypass the sizer*. The
  broader rule is more general but changes opening-order behaviour (an explicit opening quantity would
  stop being resized — a test-visible surface) and still forces the strategy to read the position and set
  the flatten quantity itself. The reducing-order rule is narrower, needs no strategy change, and is the
  precise domain statement. Chose it.
- *Fix it in the strategy* — rejected: impossible while a sizing model is installed, since the sizer
  overwrites any strategy-supplied quantity. The gap is in the broker, not the strategy.

## Consequences

- Reversing strategies work under any sizing model, including `RiskPerTradeSizing` with the entry sized
  from a fill-relative offset (ADR 0025 amendment). Reversals whose entry and exit sizes differ as equity
  or the ATR-scaled stop drifts now close exactly and re-enter freshly sized — the fixed-size symmetry a
  correct reversal used to depend on is no longer required.
- Partial exits are fixed too: an explicit scale-out quantity is honoured instead of being resized to the
  model's full-position number.
- `Portfolio` gains two public queries, `OpenQuantity(symbol)` and `ReducesOpenPosition(request)`, the
  single source of truth for the classification the broker now consults. No change to fill timing, the
  overshoot clamp, or margin.
