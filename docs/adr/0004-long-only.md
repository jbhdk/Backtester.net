# Long-only positions

The reference strategy's README describes both long and short setups, but modelling shorts
correctly requires short cash/margin accounting we have chosen to defer. For now the engine is
**long-only**: `Position.Quantity` is never negative and a Sell may only reduce or close an
existing long — a Sell that would drive quantity negative is rejected rather than silently
booking cash as it did before. This is recorded so the missing short side reads as a deliberate
scope decision, not an oversight to be "fixed" piecemeal.

## Consequences

- Strategies implement only the long side until short accounting is designed.
- Adding shorting later means revisiting `Position`/`Portfolio` accounting and this ADR.
