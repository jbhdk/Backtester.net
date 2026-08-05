using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Sizing;
using Backtester.ExecutionModels.Slippage;

namespace Backtester.Broker
{
    /// <summary>
    /// Simulates order execution against historical bar data, applying sizing, risk, slippage, and commission models.
    /// </summary>
    public class BrokerSimulator : IBroker
    {
        private readonly Portfolio _portfolio;
        private readonly IFillModel _fillModel;
        private readonly ICommissionModel _commissionModel;
        private readonly ISlippageModel _slippageModel;
        private readonly ISizingModel _sizingModel;
        // key: order ID → working order (GTC until filled or cancelled)
        private readonly Dictionary<string, Order> _orderBook = new();
        // The brackets still in play, in submission order. Each Bracket answers out of its own state — which
        // order IDs it owns, what role each leg plays, which sibling a fill cancels, which legs still rest
        // and which symbol they guard — so the broker keeps no bracket-keyed maps to hold in step. A bracket
        // retires once the position it protected is resolved, and is dropped from this list there and then.
        private readonly List<Bracket> _brackets = new();
        // key: entry order ID → the sizing stop (OrderRequest.StopPrice) a risk-sized entry declared but
        // did not arm as a bracket. Stamped onto the entry fill as its EntryStopPrice when the entry armed
        // no bracket, so a signal-exit strategy that risk-sizes still reports R (ADR 0023).
        private readonly Dictionary<string, decimal> _sizingStops = new();
        // Orders the broker declined, in attempt order, captured for audit (e.g. margin-gate rejections).
        private readonly List<RejectedOrder> _rejectedOrders = new();
        // Bracket protective-leg level changes, in record order: one when each leg is armed and one per
        // modify that moves it. The report projects a round trip's stepped stop/target line from these.
        private readonly List<BracketLevelChange> _bracketLevelChanges = new();
        private DateTime _currentBarTimestamp;
        // Monotonic counter stamped onto each order as it is created, giving a deterministic submission
        // order used to tie-break equal-priority orders when they are sequenced for fill within a bar.
        private long _submissionSequence;

        /// <summary>
        /// Initializes a new broker simulator. All model parameters are optional; defaults are applied when null.
        /// </summary>
        public BrokerSimulator(
            Portfolio portfolio,
            IFillModel fillModel = null,
            ICommissionModel commissionModel = null,
            ISlippageModel slippageModel = null,
            ISizingModel sizingModel = null)
        {
            _portfolio = portfolio;
            _fillModel = fillModel ?? new FillModel_OHLCHeuristic();
            _commissionModel = commissionModel;
            _slippageModel = slippageModel;
            _sizingModel = sizingModel;
        }

        /// <summary>
        /// Gets the orders the broker declined during the run, in attempt order, each capturing what was
        /// attempted and why (currently the Reg-T margin gate rejecting for insufficient buying power).
        /// </summary>
        public IReadOnlyList<RejectedOrder> RejectedOrders => _rejectedOrders;

        /// <summary>
        /// Gets the bracket protective-leg level changes recorded during the run, in record order: each
        /// leg's initial level when armed and a new entry per modify that trails or moves it.
        /// </summary>
        public IReadOnlyList<BracketLevelChange> BracketLevelChanges => _bracketLevelChanges;

        /// <summary>
        /// Applies sizing and risk checks, then queues the order for fill processing on the next bar.
        /// Returns the assigned order ID, or null if the order was rejected.
        /// </summary>
        internal string SubmitOrder(OrderRequest request)
        {
            return SubmitOwnedOrder(request.Copy());
        }

        /// <summary>
        /// Submits a request the broker owns outright and may write to — the sized quantity and, for a
        /// bracket entry, the sizing offset. Every public submission path hands this a copy, so the object
        /// a strategy holds is never mutated by a submission.
        /// </summary>
        private string SubmitOwnedOrder(OrderRequest request)
        {
            if (_sizingModel != null)
            {
                if (_portfolio.ReducesOpenPosition(request))
                {
                    // A reducing order closes an existing position, so its size comes from that position, not
                    // the risk model — a close is never risk-sized. A quantity-less reducing order flattens the
                    // whole position; an explicit quantity performs a partial reduce and is respected as given.
                    // Overshoot is clamped at fill, so neither can flip the position's sign.
                    if (request.Quantity <= 0)
                    {
                        request.Quantity = Math.Abs(_portfolio.OpenQuantity(request.Symbol));
                    }
                }
                else
                {
                    int sized = _sizingModel.Size(request, _portfolio);
                    // Reject any non-positive size: a sizing model that yields zero has nothing to trade, and a
                    // negative quantity would flow into the fill, Position, and RoundTrip and corrupt their
                    // prices/quantity, so no sizing model is allowed to inject one into the pipeline.
                    if (sized <= 0)
                    {
                        return null;
                    }

                    request.Quantity = sized;
                }
            }

            // Reg-T initial-margin gate, always enforced by the account. A reducing or unvaluable order
            // commits no margin and is never rejected here; an opening order must fit within buying power.
            decimal requiredMargin = _portfolio.InitialMarginForOrder(request);
            if (requiredMargin > 0m && requiredMargin > _portfolio.BuyingPower)
            {
                _rejectedOrders.Add(new RejectedOrder
                {
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Quantity = request.Quantity,
                    Price = _portfolio.ValuationPriceForOrder(request),
                    Timestamp = _currentBarTimestamp,
                    Reason = "Not enough funds"
                });
                return null;
            }

            Order order = new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Price = request.Price,
                Quantity = request.Quantity,
                SubmittedAt = _currentBarTimestamp,
                Priority = request.Priority,
                Sequence = _submissionSequence++
            };
            _orderBook[order.Id] = order;

            // Retain the sizing stop the request declared so the entry fill can be stamped with it. A
            // bracketed entry overrides this with its armed bracket stop (SubmitBracket records the pending
            // bracket), so the precedence resolves at fill time in ProcessBar.
            if (request.StopPrice.HasValue)
            {
                _sizingStops[order.Id] = request.StopPrice.Value;
            }

            return order.Id;
        }

        /// <summary>
        /// Queues a single order for fill processing. Returns the assigned order ID, or null if rejected.
        /// </summary>
        public string Submit(OrderRequest request)
        {
            return SubmitOrder(request);
        }


        /// <summary>
        /// Queues an entry order with one or two attached protective legs (a stop-loss and/or a
        /// take-profit). Returns the bracket's handle: it reports the bracket's state — pending until the
        /// entry fills, armed while its legs rest, retired once the position is resolved — and carries the
        /// order IDs the legs were booked under, each null when its leg was not requested. Ask the state, not
        /// an ID, whether the entry has filled: a target-only bracket never gets a stop order ID at all. A
        /// request that attaches neither leg
        /// cannot be constructed (an unprotected entry is a plain <see cref="Submit"/>, not a bracket), so
        /// every request that reaches here describes a legal bracket.
        /// </summary>
        public BracketHandle SubmitBracket(BracketRequest request)
        {
            // A request that reaches here is a legal bracket already — it could not have been constructed
            // otherwise — so the bracket is simply built from it.
            Bracket bracket = new(request);

            // The stop offset is the per-share risk a risk-sizing model needs, but the sizing model only sees
            // the entry OrderRequest, not this bracket. Surface the offset on the entry so RiskPerTradeSizing
            // can size a fill-relative bracket entry, whose absolute stop is not yet known (it resolves against
            // the fill later, ADR 0025). Left untouched when the caller already set an entry sizing distance.
            // The offset lands on the broker's own copy of the entry, never on the caller's request.
            OrderRequest entry = request.Entry.Copy();
            if (request.StopLeg?.Offset is decimal stopOffset && !entry.StopOffset.HasValue)
            {
                entry.StopOffset = stopOffset;
            }

            string entryId = SubmitOwnedOrder(entry);
            if (entryId == null)
            {
                return null;
            }

            bracket.AttachEntry(entryId, _orderBook[entryId].Quantity);
            _brackets.Add(bracket);
            return bracket.Handle;
        }

        /// <summary>
        /// Matches all working orders against the current bar, applies slippage and commission, and returns the resulting trades.
        /// Filled orders are removed from the book; unfilled orders remain working (GTC).
        /// Records the bar timestamp so subsequent <see cref="SubmitOrder"/> calls can stamp orders with simulation time.
        /// </summary>
        internal IEnumerable<Trade> ProcessBar(MarketSlice slice)
        {
            _currentBarTimestamp = slice.Timestamp;

            List<Order> snapshot = _orderBook.Values.ToList();
            List<Trade> trades = new();
            foreach (IGrouping<string, Order> symbolGroup in snapshot.GroupBy(o => o.Symbol))
            {
                string symbol = symbolGroup.Key;

                // Only match orders against a real bar for this symbol — one that printed at this slice's
                // timestamp. A multi-symbol run forward-fills a symbol's last real bar into slices where it
                // has no bar of its own (another symbol drove the timestamp, e.g. a 24/7 symbol producing a
                // post-close slot). Filling against that stale bar would stamp the trade at a time with no
                // real bar for this symbol (issue #56); such orders rest until the symbol's next real bar.
                if (!slice.HasRealBar(symbol))
                {
                    continue;
                }

                Candle candle = slice.BarsBySymbol[symbol];
                // Sequence the symbol's working orders before fill so that when several fill on the same
                // bar, they are applied to the portfolio in a deterministic, strategy-controllable order:
                // highest Priority first, ties broken by submission order. This lets a strategy guarantee,
                // e.g., that a flatten is applied before the reversing entry so the entry opens from flat and
                // carries its protective stop, rather than leaving intra-bar order to order-book iteration.
                IEnumerable<Order> sequenced = symbolGroup
                    .OrderByDescending(order => order.Priority)
                    .ThenBy(order => order.Sequence);
                IEnumerable<FillResult> fills = _fillModel.DetermineFills(sequenced, candle);
                foreach (FillResult fill in fills)
                {
                    ProcessFill(fill, candle, slice.Timestamp, trades);
                }
            }

            return trades;
        }

        /// <summary>
        /// Applies one fill to the portfolio: cancels its OCO sibling if any, stamps the entry stop/target,
        /// records the trade, cancels the legs left resting by a fill that flattened the position, and arms
        /// any pending bracket's protective legs. When a bracket entry arms legs, immediately attempts a
        /// same-bar fill of any leg
        /// already marketable at the bar's open (see <see cref="TryFillMarketableLegsAtArm"/>).
        /// </summary>
        private void ProcessFill(FillResult fill, Candle candle, DateTime timestamp, List<Trade> trades)
        {
            if (!_orderBook.TryGetValue(fill.OrderId, out Order filledOrder))
            {
                return;
            }

            string symbol = filledOrder.Symbol;
            _orderBook.Remove(fill.OrderId);

            // The bracket this order belongs to, if any — the pending bracket whose entry just filled, or the
            // live bracket whose protective leg it is — and what the fill means to it. One answer covers both
            // the leg role stamped onto the trade and the sibling one-cancels-the-other takes out; a
            // single-leg bracket answers "no sibling" rather than needing a branch of its own.
            Bracket bracket = BracketOwning(fill.OrderId);
            BracketFillOutcome outcome = bracket?.Fill(fill.OrderId) ?? BracketFillOutcome.None;
            BracketLeg leg = outcome.Leg;
            if (outcome.SiblingOrderId != null)
            {
                _orderBook.Remove(outcome.SiblingOrderId);
            }
            // A protective leg fill closes the position both legs guarded, so its bracket has retired itself
            // and leaves the live list; an entry fill leaves the bracket armed and still answering.
            DropIfRetired(bracket);

            decimal rawPrice = fill.Price;
            decimal adjustedPrice = _slippageModel?.Apply(rawPrice, filledOrder.Side) ?? rawPrice;
            decimal slippageAmount = Math.Abs(adjustedPrice - rawPrice);
            decimal commission = _commissionModel?.Calculate(adjustedPrice * fill.Quantity, fill.Quantity) ?? 0m;

            // The sizing stop (OrderRequest.StopPrice) is a fill-time leftover whether or not it is used, so
            // it is consumed here.
            decimal? sizingStop = _sizingStops.Remove(fill.OrderId, out decimal declaredStop) ? declaredStop : (decimal?)null;
            // A bracket entry fill completes its bracket: the bracket resolves its own legs against the actual
            // (slippage-adjusted) fill, mints their order IDs and completes its handle; the broker books them
            // further down, once the entry trade has been applied.
            IReadOnlyList<BracketLegPlacement> armedLegs = outcome.IsEntry ? bracket.Arm(adjustedPrice, filledOrder.Side) : null;
            BracketLegPlacement stopLeg = FindLeg(armedLegs, BracketLeg.StopLoss);
            BracketLegPlacement targetLeg = FindLeg(armedLegs, BracketLeg.TakeProfit);

            // Stamp the entry fill with the stop it declared, so the position freezes its initial risk as it
            // opens from flat. Precedence per ADR 0023 turns on bracket presence, not on whether the bracket
            // has a stop: a bracketed entry stamps its armed bracket stop, which is null for a target-only
            // bracket (no stop, so no initial risk). Only a non-bracketed entry falls back to the sizing stop
            // a risk-sized signal-exit entry carried (ADR 0023 amendment).
            decimal? entryStopPrice = outcome.IsEntry ? stopLeg?.Price : sizingStop;
            // The initial target is the armed bracket's take-profit level; a target exists only
            // through a bracket (there is no sizing target), so a non-bracketed entry has none.
            decimal? entryTargetPrice = targetLeg?.Price;

            Trade trade = new()
            {
                Id = fill.TradeId,
                OrderId = fill.OrderId,
                Symbol = symbol,
                Side = filledOrder.Side,
                Price = adjustedPrice,
                Quantity = fill.Quantity,
                Slippage = slippageAmount,
                Commission = commission,
                Timestamp = timestamp,
                Leg = leg,
                EntryStopPrice = entryStopPrice,
                EntryTargetPrice = entryTargetPrice
            };
            _portfolio.ApplyTrade(trade);
            trades.Add(trade);

            // Any fill that flattens the position — a strategy Signal exit, or a protective leg closing the
            // last of a position several brackets guarded — can leave another bracket's legs resting; cancel
            // them so they can never fill from flat on a later bar and open a phantom position. A leg fill
            // needs no special case: its own bracket retired and released its legs above, so it is already
            // past answering for anything here.
            if (IsFlat(symbol))
            {
                CancelRestingLegs(symbol, bracket);
            }

            if (outcome.IsEntry)
            {
                // Book the legs the bracket decided on — a single-leg bracket placed only the leg it
                // declared, so the absent leg's order ID stays null. The bracket holds the legs itself, so
                // neither the OCO pairing nor the resting set needs a map: whichever leg fills, the sibling
                // is the other one the bracket still has resting, and a signal exit asks it what is left.
                string stopId = stopLeg != null ? PlaceLeg(symbol, stopLeg) : null;
                string targetId = targetLeg != null ? PlaceLeg(symbol, targetLeg) : null;

                TryFillMarketableLegsAtArm(stopId, targetId, candle, timestamp, trades);
            }
        }

        /// <summary>
        /// Returns the live bracket that answers for the given order — the one whose entry is still pending
        /// or whose protective leg is still resting — or null when the order belongs to no bracket.
        /// </summary>
        private Bracket BracketOwning(string orderId)
        {
            return _brackets.FirstOrDefault(candidate => candidate.Owns(orderId));
        }

        /// <summary>
        /// Cancels whatever protective legs still rest against a symbol whose position has just been closed
        /// by something that was not one of those legs. Each bracket knows its own legs, and releasing a
        /// bracket's last leg retires it, so no separate per-symbol tracking is kept.
        ///
        /// The close flattens the whole position, so it flattens what *every* bracket on the symbol was
        /// guarding — a strategy may scale in with several brackets, and a leg left resting by any of them
        /// could fill from flat on a later bar and open a phantom position (#132).
        ///
        /// <paramref name="arming"/> is the bracket whose entry just filled, if any: its legs are only now
        /// being armed and guard the position that fill opened, so it is never the one being closed out.
        /// </summary>
        private void CancelRestingLegs(string symbol, Bracket arming)
        {
            foreach (Bracket bracket in ArmedBracketsOn(symbol, arming))
            {
                foreach (string legOrderId in bracket.RestingLegOrderIds)
                {
                    Cancel(legOrderId);
                }
            }
        }

        /// <summary>
        /// Returns every bracket whose legs currently guard the given symbol, oldest first. More than one can:
        /// a strategy is free to scale into a symbol with a second bracket while the first still rests, and
        /// each keeps answering for its own legs. Materialized, because cancelling a leg retires its bracket
        /// out of the live list.
        /// </summary>
        private IReadOnlyList<Bracket> ArmedBracketsOn(string symbol, Bracket excluded)
        {
            return _brackets
                .Where(candidate => candidate != excluded && candidate.State == BracketState.Armed && candidate.Symbol == symbol)
                .ToList();
        }

        /// <summary>
        /// Drops a bracket that has retired — its position resolved, no leg resting, nothing left to answer
        /// for — so the live list holds only brackets still in play. No-ops for anything else.
        /// </summary>
        private void DropIfRetired(Bracket bracket)
        {
            if (bracket != null && bracket.State == BracketState.Retired)
            {
                _brackets.Remove(bracket);
            }
        }

        /// <summary>
        /// Returns the armed leg filling the given role, or null when the bracket did not place one (or when
        /// the filled order was not a bracket entry at all).
        /// </summary>
        private static BracketLegPlacement FindLeg(IReadOnlyList<BracketLegPlacement> armedLegs, BracketLeg role)
        {
            return armedLegs?.FirstOrDefault(placement => placement.Leg == role);
        }

        /// <summary>
        /// Immediately fills a just-armed protective leg that is already marketable at the arming bar's
        /// open — the entry fill gapped through the leg's price, so a live bracket would trigger it right
        /// after the entry. The legs are evaluated against the bar collapsed to its open, so the gap-aware
        /// fill model triggers only a leg already through the market at the open (a leg the bar merely
        /// trades through later has no range here and keeps ordinary next-bar timing), and prices it at the
        /// open. Any resulting fill runs back through <see cref="ProcessFill"/> so OCO sibling-cancel and
        /// the level ledger reuse the same path; at most one leg can be through the market at the open.
        /// </summary>
        private void TryFillMarketableLegsAtArm(string stopId, string targetId, Candle candle, DateTime timestamp, List<Trade> trades)
        {
            List<Order> armedLegs = new(2);
            if (stopId != null && _orderBook.TryGetValue(stopId, out Order stopOrder))
            {
                armedLegs.Add(stopOrder);
            }
            if (targetId != null && _orderBook.TryGetValue(targetId, out Order targetOrder))
            {
                armedLegs.Add(targetOrder);
            }
            if (armedLegs.Count == 0)
            {
                return;
            }

            Candle atOpen = new()
            {
                Timestamp = candle.Timestamp,
                Open = candle.Open,
                High = candle.Open,
                Low = candle.Open,
                Close = candle.Open,
                Volume = candle.Volume
            };
            foreach (FillResult legFill in _fillModel.DetermineFills(armedLegs, atOpen).ToList())
            {
                // Re-entrant: this is ProcessFill calling back into ProcessFill, which is how a leg that is
                // marketable the instant it is armed reaches the same OCO, stamping and ledger handling as
                // any other leg fill (ADR 0025) instead of a parallel path. The depth is bounded at one
                // re-entry: the inner call carries a protective leg's fill, and a leg arms no bracket of its
                // own, so it can never reach this method again.
                ProcessFill(legFill, candle, timestamp, trades);
            }
        }

        /// <summary>
        /// Books one armed bracket leg as a working order. The bracket already decided the order ID, side,
        /// type, price and quantity and keeps the leg's role itself, so this only records the order and its
        /// initial level; the broker owns the submission sequence and the bar timestamp.
        /// </summary>
        private string PlaceLeg(string symbol, BracketLegPlacement placement)
        {
            Order order = new()
            {
                Id = placement.OrderId,
                Symbol = symbol,
                Side = placement.Side,
                Type = placement.Type,
                Price = placement.Price,
                Quantity = placement.Quantity,
                SubmittedAt = _currentBarTimestamp,
                Sequence = _submissionSequence++
            };
            _orderBook[order.Id] = order;
            RecordLevelChange(symbol, placement.Leg, placement.Price, order.Id);

            return order.Id;
        }

        /// <summary>
        /// Returns true when the symbol has no open position or its position has reduced to flat.
        /// </summary>
        private bool IsFlat(string symbol)
        {
            Position position = _portfolio.Positions.FirstOrDefault(p => p.Symbol == symbol);
            return position == null || position.Quantity == 0;
        }

        /// <summary>
        /// Appends a protective leg's level at the current bar to the ledger: its initial level when armed
        /// or a trailed/moved level on a modify.
        /// </summary>
        private void RecordLevelChange(string symbol, BracketLeg leg, decimal price, string orderId)
        {
            _bracketLevelChanges.Add(new BracketLevelChange
            {
                Symbol = symbol,
                Timestamp = _currentBarTimestamp,
                Leg = leg,
                Price = price,
                OrderId = orderId
            });
        }

        /// <summary>
        /// Removes a working order from the book so it will never fill. No-ops if the order has already filled or is unknown.
        /// </summary>
        public void Cancel(string orderId)
        {
            _orderBook.Remove(orderId);
            // A cancelled protective leg stops being its bracket's to answer for: it can no longer fill, and
            // it is no longer the sibling the remaining leg would cancel. Cancelling a bracket's entry
            // releases nothing, so the bracket stays pending exactly as it did before.
            Bracket bracket = BracketOwning(orderId);
            if (bracket != null)
            {
                bracket.Release(orderId);
                DropIfRetired(bracket);
            }
            _sizingStops.Remove(orderId);
        }


        /// <summary>
        /// Updates the trigger price of a working order. No-ops if the order has already filled or is unknown.
        /// </summary>
        public void Modify(string orderId, decimal newPrice)
        {
            if (_orderBook.TryGetValue(orderId, out Order order))
            {
                order.Price = newPrice;
                // Record the moved level only for a protective leg (a trailed stop or a moved target). The
                // owning bracket is the one that knows the role; a plain working order belongs to no bracket
                // and is not part of the ledger.
                BracketLeg leg = BracketOwning(orderId)?.RoleOf(orderId) ?? BracketLeg.None;
                if (leg != BracketLeg.None)
                {
                    RecordLevelChange(order.Symbol, leg, newPrice, orderId);
                }
            }
        }
    }
}
