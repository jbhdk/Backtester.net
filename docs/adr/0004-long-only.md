# Long-only positions

> **Status: Superseded by [ADR 0011](0011-margin-account-shorting.md).** The engine now supports
> shorting under a Reg-T initial-margin model. This ADR is retained for the history of why shorts
> were originally deferred.

The reference strategy's README describes both long and short setups, but modelling shorts
correctly requires short cash/margin accounting we have chosen to defer. For now the engine is
**long-only**: `Position.Quantity` is never negative and a Sell may only reduce or close an
existing long — a Sell that would drive quantity negative is rejected rather than silently
booking cash as it did before. This is recorded so the missing short side reads as a deliberate
scope decision, not an oversight to be "fixed" piecemeal.

## Consequences

- Strategies implement only the long side until short accounting is designed.
- Adding shorting later means revisiting `Position`/`Portfolio` accounting and this ADR.
