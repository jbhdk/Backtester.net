# backtester.net.stops

Reusable protective-stop management for the [backtester.net](https://www.nuget.org/packages/backtester.net)
engine.

A **TrailingStopManager** owns the stop lifecycle for a single bracketed trade, so a strategy can submit a
bracket and hand off exit management instead of re-implementing it. Driven one bar at a time via `OnBar`, it:

1. **Re-anchors** both protective legs onto the actual entry **fill** price the first bar the position is
   open — keeping the configured stop and target *distances* — so realized risk/reward matches the intended
   multiples even when the entry is a market order that fills at the next bar's open.
2. Moves the stop to **break-even** exactly once, as soon as profit reaches `triggerR x R`, where `1 R` is
   the re-anchored initial risk (entry fill to initial stop).
3. Ratchets a **trailing stop** once price has run `trailActivationAtrMultiple x ATR` past entry. Its
   distance interpolates from `trailDistanceAtrMultiple` (wide) down to `trailMinDistanceAtrMultiple`
   (tight) as the close advances toward the tightening reference, and **never loosens**.

## The tightening reference is in R, not the target

The trail tightens over a reference expressed as `trailTightenR x R` — the same risk currency the
break-even rule uses. It is **independent of the take-profit target**: a trade can be told to reach full
stop-tightness before its target (`trailTightenR` < the target's R multiple), at it, or beyond it. The
target is still re-anchored as the take-profit leg; it simply no longer dictates how the stop tightens.

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
    triggerR: 1.0m,            // break-even at +1R
    stopDistance,
    targetDistance,
    trailTightenR: 3.0m,       // fully tightened by +3R, wherever the target sits
    trailActivationAtrMultiple: 2.0m,
    trailDistanceAtrMultiple: 4.0m,
    trailMinDistanceAtrMultiple: 2.5m,
    enableManagement: true);

// Each bar, for the managed symbol:
manager.OnBar(inPosition, bar.Close, averageEntryPrice, atr, broker);
if (manager.IsFinished)
{
    // The trade opened and has closed; drop the manager and become eligible to re-enter.
}
```

`enableManagement: false` keeps the bracket fully **static** after re-anchoring — no break-even, no trail —
so an entry signal's edge can be measured against an unmanaged exit.

This package references `backtester.net` and nothing else: it modifies a submitted bracket's resting legs
through `IBroker`/`BracketHandle` and speaks the engine's `PositionDirection` vocabulary.
