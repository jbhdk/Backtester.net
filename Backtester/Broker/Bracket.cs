using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// One submitted bracket: the entry order, the one or two protective legs attached to it, and the rules
    /// that place those legs once the entry fills. A bracket is created from a <see cref="BracketRequest"/>
    /// at submit time — which is where a request that cannot form a legal bracket is rejected — stays pending
    /// while its entry works, and arms its legs against the actual entry fill.
    ///
    /// The bracket decides and the broker executes: <see cref="Arm"/> resolves each leg to an absolute price,
    /// mints the leg order IDs and completes the <see cref="BracketHandle"/>, but never touches the order
    /// book. Turning each placement into a working order is the broker's job, which is what lets a bracket be
    /// driven with no broker, no portfolio and no market data. Once armed the bracket keeps answering for its
    /// legs — their roles, which sibling a fill cancels, and which of them still rest — until none is left,
    /// at which point it retires itself and the broker forgets it.
    /// </summary>
    internal sealed class Bracket
    {
        private readonly string _symbol;
        private readonly decimal? _stopPrice;
        private readonly decimal? _targetPrice;
        private readonly decimal? _stopOffset;
        private readonly decimal? _targetOffset;
        // key: order ID of a protective leg that is still resting → the role it plays. Holds the legs this
        // bracket armed and that have neither filled nor been cancelled, so it is at most two entries, and
        // one leg's sibling is simply the other one still in here.
        private readonly Dictionary<string, BracketLeg> _restingLegs = new();
        private string _entryOrderId;
        private int _quantity;

        private Bracket(BracketRequest request)
        {
            _symbol = request.Entry.Symbol;
            _stopPrice = request.StopPrice;
            _targetPrice = request.TargetPrice;
            _stopOffset = request.StopOffset;
            _targetOffset = request.TargetOffset;
            Handle = new BracketHandle();
        }

        /// <summary>
        /// Gets the handle held by the strategy that submitted this bracket. Its entry order ID is set when
        /// the entry is accepted; its stop and target order IDs are filled in by <see cref="Arm"/>, and each
        /// stays null for a leg this bracket was never given.
        /// </summary>
        internal BracketHandle Handle { get; }

        /// <summary>
        /// Gets the symbol this bracket trades — its entry's, and therefore its legs' and the position they
        /// protect. It is how the broker finds the bracket guarding a position that something else closed.
        /// </summary>
        internal string Symbol => _symbol;

        /// <summary>
        /// Gets where this bracket stands: waiting on its entry, protecting an open position, or done.
        /// </summary>
        internal BracketState State { get; private set; } = BracketState.Pending;

        /// <summary>
        /// Gets a snapshot of the order IDs of the protective legs still resting — none once the bracket has
        /// retired, and at most two before that. A snapshot, so the caller can cancel each one in turn while
        /// the bracket releases them.
        /// </summary>
        internal IReadOnlyList<string> RestingLegOrderIds => _restingLegs.Keys.ToList();

        /// <summary>
        /// Creates the bracket a request describes, rejecting a request that cannot form a legal bracket:
        /// a leg given in both the absolute and the fill-relative form, a non-positive offset, or a request
        /// with no leg at all (an unprotected entry is a plain order, not a bracket). This is caller misuse
        /// and throws, unlike the funds rejection that declines an order by returning null.
        /// </summary>
        internal static Bracket Create(BracketRequest request)
        {
            if (request.StopPrice.HasValue && request.StopOffset.HasValue)
            {
                throw new ArgumentException("The stop leg cannot be given as both an absolute price and an offset.", nameof(request));
            }
            if (request.TargetPrice.HasValue && request.TargetOffset.HasValue)
            {
                throw new ArgumentException("The target leg cannot be given as both an absolute price and an offset.", nameof(request));
            }
            if (request.StopOffset.HasValue && request.StopOffset.Value <= 0m)
            {
                throw new ArgumentException("The stop offset must be greater than zero.", nameof(request));
            }
            if (request.TargetOffset.HasValue && request.TargetOffset.Value <= 0m)
            {
                throw new ArgumentException("The target offset must be greater than zero.", nameof(request));
            }

            bool hasStop = request.StopPrice.HasValue || request.StopOffset.HasValue;
            bool hasTarget = request.TargetPrice.HasValue || request.TargetOffset.HasValue;
            if (!hasStop && !hasTarget)
            {
                throw new ArgumentException("A bracket must have at least one leg (a stop-loss and/or a take-profit).", nameof(request));
            }

            return new Bracket(request);
        }

        /// <summary>
        /// Binds the accepted entry order to this bracket and records the size its legs will cover — the
        /// entry's sized quantity, which is known only once the entry has been accepted. Called before the
        /// bracket goes live, so the handle carries the entry order ID the moment the strategy receives it.
        /// </summary>
        internal void AttachEntry(string entryOrderId, int quantity)
        {
            _entryOrderId = entryOrderId;
            _quantity = quantity;
            Handle.EntryOrderId = entryOrderId;
        }

        /// <summary>
        /// Returns true when the given order ID is one this bracket is currently waiting on: its entry while
        /// the bracket is still pending, or one of its resting protective legs once it has armed. An armed
        /// bracket has consumed its entry and owns it no longer, and a leg that filled or was cancelled drops
        /// out, so the broker can find the bracket an order belongs to without keeping an index in step.
        /// </summary>
        internal bool Owns(string orderId)
        {
            return (State == BracketState.Pending && _entryOrderId != null && _entryOrderId == orderId)
                || _restingLegs.ContainsKey(orderId);
        }

        /// <summary>
        /// Returns the protective role of one of this bracket's resting legs — what a fill from it means and
        /// which line of the level ledger a modify moves. None for anything that is not a resting leg: the
        /// entry, or a leg that has already filled or been cancelled.
        /// </summary>
        internal BracketLeg RoleOf(string orderId)
        {
            return _restingLegs.TryGetValue(orderId, out BracketLeg leg) ? leg : BracketLeg.None;
        }

        /// <summary>
        /// Reports that one of this bracket's orders filled, and answers what that means. The entry fill
        /// leaves the bracket ready to <see cref="Arm"/>. A protective leg fill closes the position both legs
        /// protected, so it takes the other one with it — one-cancels-the-other — and a bracket that armed a
        /// single leg answers with no sibling rather than needing a path of its own (ADR 0002). Either way
        /// the bracket stops answering for the orders involved. Call only for an order it <see cref="Owns"/>.
        /// </summary>
        internal BracketFillOutcome Fill(string orderId)
        {
            if (_restingLegs.TryGetValue(orderId, out BracketLeg leg))
            {
                // At most one other leg can be resting, so the sibling is whatever remains once the filled
                // leg is taken out — and nothing rests afterwards either way, which resolves the position
                // this bracket protected and retires it.
                _restingLegs.Remove(orderId);
                string siblingOrderId = _restingLegs.Keys.FirstOrDefault();
                _restingLegs.Clear();
                State = BracketState.Retired;

                return new BracketFillOutcome { Leg = leg, SiblingOrderId = siblingOrderId };
            }

            // Not a leg, so it is the entry — the only other order a bracket ever owns.
            return new BracketFillOutcome { IsEntry = true };
        }

        /// <summary>
        /// Drops a leg the broker cancelled: it can no longer fill, and it is no longer a sibling for the
        /// remaining leg to cancel. Releasing the last resting leg leaves nothing guarding the position, so
        /// the bracket retires. A bracket's entry is not a leg, so cancelling that leaves it pending.
        /// </summary>
        internal void Release(string orderId)
        {
            _restingLegs.Remove(orderId);
            if (State == BracketState.Armed && _restingLegs.Count == 0)
            {
                State = BracketState.Retired;
            }
        }

        /// <summary>
        /// Arms the protective legs against the actual (slippage-adjusted) entry fill and returns their
        /// placements — none, one or two, stop first — completing the handle with the IDs it minted.
        ///
        /// Each leg is resolved to an absolute price: an absolute leg resolves to itself, while a
        /// fill-relative leg is placed at its requested distance on the protective side of the fill — a long
        /// entry's stop below and target above, a short entry's mirrored (ADR 0025). Resolving against the
        /// real fill makes the realized stop distance, and therefore initial risk and R, equal the requested
        /// offset exactly regardless of any gap between decision and fill. Protective legs close the entry,
        /// so they take the opposite side: a long entry arms Sell legs, a short entry arms Buy legs.
        /// </summary>
        internal IReadOnlyList<BracketLegPlacement> Arm(decimal fillPrice, OrderSide entrySide)
        {
            bool isLongEntry = entrySide == OrderSide.Buy;
            OrderSide legSide = isLongEntry ? OrderSide.Sell : OrderSide.Buy;
            decimal? stopPrice = ResolveLegPrice(_stopPrice, _stopOffset, fillPrice, isLongEntry ? -1m : 1m);
            decimal? targetPrice = ResolveLegPrice(_targetPrice, _targetOffset, fillPrice, isLongEntry ? 1m : -1m);

            List<BracketLegPlacement> placements = new(2);
            if (stopPrice.HasValue)
            {
                placements.Add(NewPlacement(legSide, OrderType.Stop, stopPrice.Value, BracketLeg.StopLoss));
                Handle.StopOrderId = placements[placements.Count - 1].OrderId;
            }
            if (targetPrice.HasValue)
            {
                placements.Add(NewPlacement(legSide, OrderType.Limit, targetPrice.Value, BracketLeg.TakeProfit));
                Handle.TargetOrderId = placements[placements.Count - 1].OrderId;
            }

            State = BracketState.Armed;
            return placements;
        }

        /// <summary>
        /// Describes one leg to place, minting the order ID the broker will book it under and taking the leg
        /// on as this bracket's own: from here it answers for the leg's role and for whether a fill on the
        /// other leg cancels it.
        /// </summary>
        private BracketLegPlacement NewPlacement(OrderSide side, OrderType type, decimal price, BracketLeg leg)
        {
            BracketLegPlacement placement = new()
            {
                OrderId = Guid.NewGuid().ToString(),
                Side = side,
                Type = type,
                Price = price,
                Quantity = _quantity,
                Leg = leg
            };
            _restingLegs[placement.OrderId] = leg;

            return placement;
        }

        /// <summary>
        /// Resolves one leg to an absolute trigger price: the absolute price when one was given, otherwise
        /// the fill-relative offset applied to the actual fill on the protective side
        /// (<paramref name="offsetSign"/> is -1/+1 to subtract or add), or null when the leg was not
        /// requested in either form.
        /// </summary>
        private static decimal? ResolveLegPrice(decimal? absolutePrice, decimal? offset, decimal fillPrice, decimal offsetSign)
        {
            if (absolutePrice.HasValue)
            {
                return absolutePrice.Value;
            }
            if (offset.HasValue)
            {
                return fillPrice + offsetSign * offset.Value;
            }
            return null;
        }
    }
}
