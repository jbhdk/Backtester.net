# backtester.net.stops

Reusable protective-stop management for the [backtester.net](https://www.nuget.org/packages/backtester.net)
engine.

A **TrailingStopManager** owns the stop lifecycle for a single bracketed trade, so a strategy can submit a
bracket and hand off exit management instead of re-implementing it. Driven one bar at a time via `OnBar`, it:

1. **Re-anchors** both protective legs onto the actual entry **fill** price the first bar the position is
   open — keeping the configured stop and target *distances* — so realized risk/reward matches the intended
   multiples even when the entry is a market order that fills at the next bar's open.
2. Applies a single R-based rule the first close whose profit reaches `triggerR x R`, where `1 R` is the
   re-anchored initial risk (entry fill to initial stop): the stop moves to **break-even** and the
   **trailing stop arms** in the same step — one modify, placing the stop at the better of break-even and
   the trail stop.
3. The armed trail ratchets a stop whose distance — in multiples of R — interpolates from `trailDistanceR`
   (wide) down to `trailMinDistanceR` (tight) as the close advances toward the tightening reference, and
   **never loosens**; break-even remains a floor beneath it. The constructor rejects
   `trailDistanceR < trailMinDistanceR` — an inverted pair would widen the trail as profit grows instead of
   tightening it. Equal values are legal (a constant-distance trail).

## Everything is in R

Break-even (`triggerR`), the tightening reference (`trailTightenR`), and both trail distances
(`trailDistanceR`, `trailMinDistanceR`) all speak the same risk currency: multiples of the re-anchored
initial risk. The manager reads no ATR — every distance is frozen at entry in units of R, so the trail does
not adapt to later volatility (the volatility view lives in R itself when the caller derives the stop
distance from ATR at signal time).

## The tightening reference is in R, not the target

The trail tightens over a reference expressed as `trailTightenR x R`. It is **independent of the
take-profit target**: a trade can be told to reach full stop-tightness before its target
(`trailTightenR` < the target's R multiple), at it, or beyond it. The target is still re-anchored as the
take-profit leg; it simply does not dictate how the stop tightens.

```csharp
using Backtester.Broker;
using Backtester.Core;
using Backtester.Stops;

// Submit your bracket, then hand its handle to the manager.
decimal stopDistance   = (decimal)atr * stopAtrMultiple;
decimal targetDistance = (decimal)atr * targetAtrMultiple;

TrailingStopManager manager = new TrailingStopManager(
    handle,
    initialStopPrice: stopPrice,
    direction: PositionDirection.Long,
    triggerR: 1.0m,            // break-even and trail arm at +1R
    stopDistance,
    targetDistance,
    trailTightenR: 3.0m,       // fully tightened by +3R, wherever the target sits
    trailDistanceR: 2.0m,      // trail 2R behind the close when far from the reference
    trailMinDistanceR: 1.25m,  // trail 1.25R behind the close at the reference
    enableManagement: true);

// Each bar, for the managed symbol:
manager.OnBar(bar.Close, averageEntryPrice, broker);
if (manager.IsFinished)
{
    // The bracket has retired; drop the manager and become eligible to re-enter.
}
```

The manager takes the trade's lifecycle from the handle's `State` — it does nothing while the bracket is
`Pending`, manages the stop while it is `Armed`, and reports `IsFinished` once it is `Retired`. Ask the
handle the same question before re-entering, rather than testing whether an order id happens to be set.

`enableManagement: false` keeps the bracket fully **static** after re-anchoring — no break-even, no trail —
so an entry signal's edge can be measured against an unmanaged exit.

## Upgrading within 2.x

`OnBar` no longer takes an `inPosition` flag: the bracket's handle now reports its own state, so the
caller no longer has to tell the manager whether a position is open. Drop the argument at each call site.

## Upgrading from 1.x

Version 2 is a clean break: `trailActivationAtrMultiple` is gone (`triggerR` arms the trail),
`trailDistanceAtrMultiple`/`trailMinDistanceAtrMultiple` are now `trailDistanceR`/`trailMinDistanceR` in
multiples of R rather than ATR, and `OnBar` no longer takes an `atr` argument. There is no faithful
ATR-to-R translation for an existing configuration — re-derive the multiples against your stop distance
(e.g. with a 2-ATR stop, an old 4-ATR trail distance is `trailDistanceR: 2.0m`).

This package references `backtester.net` and nothing else: it modifies a submitted bracket's resting legs
through `IBroker`/`BracketHandle` and speaks the engine's `PositionDirection` vocabulary.
