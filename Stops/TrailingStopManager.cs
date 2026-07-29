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
    /// entry. Thereafter, once the trade's bar close shows a profit of at least <c>triggerR x R</c> (where 1 R
    /// is the re-anchored initial risk, the distance from the entry fill to the initial stop), it moves the
    /// stop to the entry price exactly once (break-even). Then, once price has run
    /// <c>trailActivationAtrMultiple x ATR</c> past entry, it ratchets a trailing stop whose distance
    /// interpolates from <c>trailDistanceAtrMultiple</c> (far from the tightening reference) down to
    /// <c>trailMinDistanceAtrMultiple</c> (at the reference), tightening the closer price runs to the
    /// <c>trailTightenR x R</c> reference and never loosening. That reference is deliberately independent of
    /// the take-profit target: a trade can be told to reach full stop-tightness well before (or after) its
    /// target. A tiny state machine (awaiting fill, open, finished) makes the manager robust to the engine's
    /// fill/OnBar ordering and lets the owning strategy avoid entering on top of a submitted but not-yet-filled
    /// bracket.
    /// </summary>
    public sealed class TrailingStopManager
    {
        private readonly BracketHandle _handle;
        private readonly PositionDirection _direction;
        private readonly decimal _triggerR;
        private readonly decimal _stopDistance;
        private readonly decimal _targetDistance;
        private readonly decimal _trailTightenR;
        private readonly decimal _trailActivationAtrMultiple;
        private readonly decimal _trailDistanceAtrMultiple;
        private readonly decimal _trailMinDistanceAtrMultiple;
        private readonly bool _enableManagement;

        private decimal _initialStopPrice;
        private bool _opened;
        private bool _finished;
        private bool _reanchored;
        private bool _breakEvenApplied;
        private decimal _currentStop;

        /// <summary>
        /// Initialises the manager for a freshly submitted bracket. The handle's stop order id is populated
        /// by the broker once the entry fills; until then every stop move is suppressed. The trail's
        /// interpolation runs from <paramref name="trailDistanceAtrMultiple"/> down to
        /// <paramref name="trailMinDistanceAtrMultiple"/>, so the former must be at least the latter — an
        /// inverted pair would make the trail widen as profit grows, the opposite of the documented ratchet,
        /// and is rejected here rather than run silently. Equal values are legal: a constant-distance trail.
        /// </summary>
        /// <param name="handle">The submitted bracket's order handle (its stop and target order ids are read when moving the legs).</param>
        /// <param name="initialStopPrice">The protective stop price placed at submission (trigger-close anchored); re-anchored onto the fill price once the entry fills.</param>
        /// <param name="direction">The trade's direction, used to measure profit and ratchet on the correct side.</param>
        /// <param name="triggerR">The profit, in multiples of the initial risk R, at which the stop moves to break-even.</param>
        /// <param name="stopDistance">The frozen stop distance (e.g. ATR x StopAtrMultiple); the stop is re-anchored to this distance from the fill price.</param>
        /// <param name="targetDistance">The frozen target distance (e.g. ATR x TargetAtrMultiple); the target is re-anchored to this distance from the fill price.</param>
        /// <param name="trailTightenR">The profit, in multiples of the initial risk R, at which the trail reaches its minimum distance. This is the tightening reference — independent of the take-profit target — over which the trail distance interpolates from <paramref name="trailDistanceAtrMultiple"/> down to <paramref name="trailMinDistanceAtrMultiple"/>.</param>
        /// <param name="trailActivationAtrMultiple">Profit past entry, in ATR multiples, at which the trail activates.</param>
        /// <param name="trailDistanceAtrMultiple">Trail distance below the close, in ATR multiples, when furthest from the tightening reference.</param>
        /// <param name="trailMinDistanceAtrMultiple">Trail distance below the close, in ATR multiples, once the close reaches the tightening reference.</param>
        /// <param name="enableManagement">When false the bracket stays fully static after re-anchoring — no break-even move, no trail — so the entry's edge is measured against an unmanaged exit.</param>
        public TrailingStopManager(
            BracketHandle handle,
            decimal initialStopPrice,
            PositionDirection direction,
            decimal triggerR,
            decimal stopDistance,
            decimal targetDistance,
            decimal trailTightenR,
            decimal trailActivationAtrMultiple,
            decimal trailDistanceAtrMultiple,
            decimal trailMinDistanceAtrMultiple,
            bool enableManagement)
        {
            if (trailDistanceAtrMultiple < trailMinDistanceAtrMultiple)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trailDistanceAtrMultiple),
                    trailDistanceAtrMultiple,
                    $"Trail distance ATR multiple ({trailDistanceAtrMultiple}) must be at least the trail min distance ATR multiple ({trailMinDistanceAtrMultiple}); an inverted pair would loosen the trail as profit grows instead of tightening it.");
            }

            _handle = handle;
            _initialStopPrice = initialStopPrice;
            _direction = direction;
            _triggerR = triggerR;
            _stopDistance = stopDistance;
            _targetDistance = targetDistance;
            _trailTightenR = trailTightenR;
            _trailActivationAtrMultiple = trailActivationAtrMultiple;
            _trailDistanceAtrMultiple = trailDistanceAtrMultiple;
            _trailMinDistanceAtrMultiple = trailMinDistanceAtrMultiple;
            _enableManagement = enableManagement;
            _currentStop = initialStopPrice;
        }

        /// <summary>True once the managed trade has opened and then closed; the owner should drop the manager.</summary>
        public bool IsFinished => _finished;

        /// <summary>
        /// Drives the protective-stop lifecycle for the current bar. Records the open transition, re-anchors both
        /// protective legs onto the fill price the first bar the position is open, detects the close, moves the
        /// stop to break-even once the profit reaches the configured R multiple, and then ratchets the trailing
        /// stop once price has run far enough past entry. Acting on the bar close introduces no lookahead: a
        /// modified stop becomes active on the following bar.
        /// </summary>
        /// <param name="inPosition">Whether an open position exists for the trade's symbol on this bar.</param>
        /// <param name="close">The current bar's close, against which profit and the trail are measured.</param>
        /// <param name="averagePrice">The position's average entry price; ignored while flat.</param>
        /// <param name="atr">The current bar's ATR used to size the trail distance; the trail is skipped when absent.</param>
        /// <param name="broker">The broker used to modify the resting stop order.</param>
        public void OnBar(bool inPosition, decimal close, decimal averagePrice, double? atr, IBroker broker)
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

            // Break-even phase: move the stop to the entry price exactly once, then defer to the trail.
            if (!_breakEvenApplied)
            {
                decimal risk = Math.Abs(averagePrice - _initialStopPrice);
                if (risk <= 0m)
                {
                    return;
                }

                decimal profit = _direction == PositionDirection.Long ? close - averagePrice : averagePrice - close;
                if (profit >= _triggerR * risk)
                {
                    broker.Modify(_handle.StopOrderId, averagePrice);
                    _breakEvenApplied = true;
                    _currentStop = averagePrice;
                }

                return;
            }

            // Trail phase: needs an ATR to size the distance.
            if (atr is null)
            {
                return;
            }

            // Sign mirrors every comparison so a long stop only ever rises and a short stop only ever falls.
            int direction = _direction == PositionDirection.Long ? 1 : -1;
            double atrValue = atr.Value;

            decimal activation = averagePrice + direction * (decimal)((double)_trailActivationAtrMultiple * atrValue);
            if (direction * close < direction * activation)
            {
                return;
            }

            // Fraction of the entry->reference span the close has covered, clamped to [0, 1]. The reference is
            // trailTightenR multiples of the initial risk R (entry fill to the re-anchored stop) — independent
            // of the take-profit target. As the fraction approaches 1 the trail distance is drawn from
            // TrailDistanceAtrMultiple toward TrailMinDistanceAtrMultiple, so the stop tightens the closer price
            // runs to that reference.
            decimal initialRisk = Math.Abs(averagePrice - _initialStopPrice);
            decimal referenceSpan = _trailTightenR * initialRisk;
            double progress = 0.0;
            if (referenceSpan > 0m)
            {
                progress = Math.Clamp((double)(direction * (close - averagePrice) / referenceSpan), 0.0, 1.0);
            }

            decimal distanceMultiple = _trailDistanceAtrMultiple
                - (decimal)progress * (_trailDistanceAtrMultiple - _trailMinDistanceAtrMultiple);

            decimal newStop = close - direction * (decimal)((double)distanceMultiple * atrValue);
            if (direction * newStop > direction * _currentStop && direction * newStop > direction * _initialStopPrice)
            {
                broker.Modify(_handle.StopOrderId, newStop);
                _currentStop = newStop;
            }
        }
    }
}
