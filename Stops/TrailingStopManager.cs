using System;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Stops
{
    /// <summary>
    /// Manages the protective-stop lifecycle for a single bracketed trade. The bracket is submitted with its
    /// stop and target anchored on the trigger bar's close, but the entry is a market order that fills on the
    /// next bar at that bar's open. So, the moment the entry fills, this manager re-anchors both protective
    /// legs onto the actual fill price (the entry candle's open) — keeping the configured stop and target
    /// <em>distances</em> unchanged — so realized risk/reward matches the configured multiples around the true
    /// entry. Thereafter a single R-denominated rule manages the stop (where 1 R is the re-anchored initial
    /// risk, the distance from the entry fill to the initial stop): once the trade's bar close shows a profit
    /// of at least <c>triggerR x R</c>, the stop moves to break-even and the trail arms in the same step. The
    /// armed trail ratchets a stop whose distance interpolates from <c>trailDistanceR x R</c> (far from the
    /// tightening reference) down to <c>trailMinDistanceR x R</c> (at the reference), tightening the closer
    /// price runs to the <c>trailTightenR x R</c> reference and never loosening; break-even acts as a floor
    /// under the trail. That reference is deliberately independent of the take-profit target: a trade can be
    /// told to reach full stop-tightness well before (or after) its target. Every distance is frozen at entry
    /// in units of R — the manager reads no ATR and does not adapt to later volatility. A tiny state machine
    /// (awaiting fill, open, finished) makes the manager robust to the engine's fill/OnBar ordering and lets
    /// the owning strategy avoid entering on top of a submitted but not-yet-filled bracket.
    /// </summary>
    public sealed class TrailingStopManager
    {
        private readonly BracketHandle _handle;
        private readonly PositionDirection _direction;
        private readonly decimal _triggerR;
        private readonly decimal _stopDistance;
        private readonly decimal _targetDistance;
        private readonly decimal _trailTightenR;
        private readonly decimal _trailDistanceR;
        private readonly decimal _trailMinDistanceR;
        private readonly bool _enableManagement;

        private decimal _initialStopPrice;
        private bool _opened;
        private bool _finished;
        private bool _reanchored;
        private bool _armed;
        private decimal _currentStop;

        /// <summary>
        /// Initialises the manager for a freshly submitted bracket. The handle's stop order id is populated
        /// by the broker once the entry fills; until then every stop move is suppressed. The trail's
        /// interpolation runs from <paramref name="trailDistanceR"/> down to
        /// <paramref name="trailMinDistanceR"/>, so the former must be at least the latter — an
        /// inverted pair would make the trail widen as profit grows, the opposite of the documented ratchet,
        /// and is rejected here rather than run silently. Equal values are legal: a constant-distance trail.
        /// </summary>
        /// <param name="handle">The submitted bracket's order handle (its stop and target order ids are read when moving the legs).</param>
        /// <param name="initialStopPrice">The protective stop price placed at submission (trigger-close anchored); re-anchored onto the fill price once the entry fills.</param>
        /// <param name="direction">The trade's direction, used to measure profit and ratchet on the correct side.</param>
        /// <param name="triggerR">The profit, in multiples of the initial risk R, at which the stop moves to break-even and the trail arms.</param>
        /// <param name="stopDistance">The frozen stop distance (e.g. ATR x StopAtrMultiple at signal time); the stop is re-anchored to this distance from the fill price, and it becomes 1 R.</param>
        /// <param name="targetDistance">The frozen target distance (e.g. ATR x TargetAtrMultiple at signal time); the target is re-anchored to this distance from the fill price.</param>
        /// <param name="trailTightenR">The profit, in multiples of the initial risk R, at which the trail reaches its minimum distance. This is the tightening reference — independent of the take-profit target — over which the trail distance interpolates from <paramref name="trailDistanceR"/> down to <paramref name="trailMinDistanceR"/>.</param>
        /// <param name="trailDistanceR">Trail distance behind the close, in multiples of R, when furthest from the tightening reference.</param>
        /// <param name="trailMinDistanceR">Trail distance behind the close, in multiples of R, once the close reaches the tightening reference.</param>
        /// <param name="enableManagement">When false the bracket stays fully static after re-anchoring — no break-even move, no trail — so the entry's edge is measured against an unmanaged exit.</param>
        public TrailingStopManager(
            BracketHandle handle,
            decimal initialStopPrice,
            PositionDirection direction,
            decimal triggerR,
            decimal stopDistance,
            decimal targetDistance,
            decimal trailTightenR,
            decimal trailDistanceR,
            decimal trailMinDistanceR,
            bool enableManagement)
        {
            if (trailDistanceR < trailMinDistanceR)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trailDistanceR),
                    trailDistanceR,
                    $"Trail distance R multiple ({trailDistanceR}) must be at least the trail min distance R multiple ({trailMinDistanceR}); an inverted pair would loosen the trail as profit grows instead of tightening it.");
            }

            _handle = handle;
            _initialStopPrice = initialStopPrice;
            _direction = direction;
            _triggerR = triggerR;
            _stopDistance = stopDistance;
            _targetDistance = targetDistance;
            _trailTightenR = trailTightenR;
            _trailDistanceR = trailDistanceR;
            _trailMinDistanceR = trailMinDistanceR;
            _enableManagement = enableManagement;
            _currentStop = initialStopPrice;
        }

        /// <summary>True once the managed trade has opened and then closed; the owner should drop the manager.</summary>
        public bool IsFinished => _finished;

        /// <summary>
        /// Drives the protective-stop lifecycle for the current bar. Records the open transition, re-anchors both
        /// protective legs onto the fill price the first bar the position is open, detects the close, and — once
        /// the profit reaches <c>triggerR x R</c> — moves the stop to break-even and arms the ratcheting trail.
        /// Acting on the bar close introduces no lookahead: a modified stop becomes active on the following bar.
        /// </summary>
        /// <param name="inPosition">Whether an open position exists for the trade's symbol on this bar.</param>
        /// <param name="close">The current bar's close, against which profit and the trail are measured.</param>
        /// <param name="averagePrice">The position's average entry price; ignored while flat.</param>
        /// <param name="broker">The broker used to modify the resting stop order.</param>
        public void OnBar(bool inPosition, decimal close, decimal averagePrice, IBroker broker)
        {
            if (_finished)
            {
                return;
            }

            if (!_opened)
            {
                // Still waiting for the entry market order to fill; nothing to manage until it does.
                if (!inPosition)
                {
                    return;
                }

                _opened = true;
            }
            else if (!inPosition)
            {
                // The trade opened and has now closed (stop or target hit); retire the manager.
                _finished = true;
                return;
            }

            if (_handle.StopOrderId is null)
            {
                return;
            }

            // Re-anchor phase: the bracket was submitted with its legs anchored on the trigger bar's close, but
            // the entry actually filled at this bar's open. Re-anchor both legs onto the fill price (keeping the
            // frozen distances) exactly once, then consume the bar so break-even and the trail are measured
            // from the true entry on the following bars. The modified levels become active on the next bar, so
            // the stale trigger-close levels never get a chance to trigger first.
            if (!_reanchored)
            {
                int sign = _direction == PositionDirection.Long ? 1 : -1;
                decimal reanchoredStop = averagePrice - sign * _stopDistance;
                decimal reanchoredTarget = averagePrice + sign * _targetDistance;

                broker.Modify(_handle.StopOrderId, reanchoredStop);
                if (_handle.TargetOrderId is not null)
                {
                    broker.Modify(_handle.TargetOrderId, reanchoredTarget);
                }

                _initialStopPrice = reanchoredStop;
                _currentStop = reanchoredStop;
                _reanchored = true;
                return;
            }

            // Static-bracket mode: management is off, so the re-anchored stop and target stay put for the life
            // of the trade — no break-even, no trail. Re-anchoring above still runs because it corrects the
            // legs onto the true fill price; it is not stop management.
            if (!_enableManagement)
            {
                return;
            }

            // 1 R is the re-anchored initial risk: the distance from the entry fill to the re-anchored stop.
            decimal risk = Math.Abs(averagePrice - _initialStopPrice);
            if (risk <= 0m)
            {
                return;
            }

            // Sign mirrors every comparison so a long stop only ever rises and a short stop only ever falls.
            int direction = _direction == PositionDirection.Long ? 1 : -1;
            decimal profit = direction * (close - averagePrice);

            // Arming: the first close at or past triggerR x R arms the trail; the same pass below then places
            // the stop at the better of break-even and the trail stop — a single modify, no lagged bar.
            if (!_armed)
            {
                if (profit < _triggerR * risk)
                {
                    return;
                }

                _armed = true;
            }

            // Fraction of the entry->reference span the close has covered, clamped to [0, 1]. The reference is
            // trailTightenR multiples of R — independent of the take-profit target. As the fraction approaches
            // 1 the trail distance is drawn from trailDistanceR toward trailMinDistanceR, so the stop tightens
            // the closer price runs to that reference.
            decimal referenceSpan = _trailTightenR * risk;
            decimal progress = 0m;
            if (referenceSpan > 0m)
            {
                progress = Math.Clamp(profit / referenceSpan, 0m, 1m);
            }

            decimal distanceMultiple = _trailDistanceR - progress * (_trailDistanceR - _trailMinDistanceR);

            decimal newStop = close - direction * distanceMultiple * risk;

            // Break-even is a floor under the armed trail: the stop never sits on the losing side of entry.
            if (direction * newStop < direction * averagePrice)
            {
                newStop = averagePrice;
            }

            if (direction * newStop > direction * _currentStop && direction * newStop > direction * _initialStopPrice)
            {
                broker.Modify(_handle.StopOrderId, newStop);
                _currentStop = newStop;
            }
        }
    }
}
