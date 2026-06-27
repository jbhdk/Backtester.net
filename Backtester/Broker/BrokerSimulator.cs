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
    public class BrokerSimulator : IBrokerSimulator, IBroker
    {
        private readonly Portfolio _portfolio;
        private readonly IFillModel _fillModel;
        private readonly ICommissionModel _commissionModel;
        private readonly ISlippageModel _slippageModel;
        private readonly ISizingModel _sizingModel;
        // key: order ID → working order (GTC until filled or cancelled)
        private readonly Dictionary<string, Order> _orderBook = new();
        // key: entry order ID → (stopPrice, targetPrice, quantity, handle) for pending bracket legs
        private readonly Dictionary<string, (decimal stopPrice, decimal targetPrice, int quantity, BracketHandle handle)> _pendingBrackets = new();
        // key: order ID → sibling order ID for OCO pairs (stop ↔ target)
        private readonly Dictionary<string, string> _ocoLinks = new();
        // key: order ID → its bracket leg role, recorded when a protective leg is armed so the fill it
        // produces can be stamped (the round trip's exit reason is derived from this).
        private readonly Dictionary<string, BracketLeg> _legRoles = new();
        // Orders the broker declined, in attempt order, captured for audit (e.g. margin-gate rejections).
        private readonly List<RejectedOrder> _rejectedOrders = new();
        // Bracket protective-leg level changes, in record order: one when each leg is armed and one per
        // modify that moves it. The report projects a round trip's stepped stop/target line from these.
        private readonly List<BracketLevelChange> _bracketLevelChanges = new();
        private DateTime _currentBarTimestamp;

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
        public string SubmitOrder(OrderRequest request)
        {
            if (_sizingModel != null)
            {
                int sized = _sizingModel.Size(request, _portfolio);
                if (sized == 0)
                {
                    return null;
                }

                request.Quantity = sized;
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
                SubmittedAt = _currentBarTimestamp
            };
            _orderBook[order.Id] = order;

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
        /// Queues an entry order with attached stop-loss and take-profit. Returns a handle whose
        /// StopOrderId and TargetOrderId are populated once the entry fills.
        /// </summary>
        public BracketHandle SubmitBracket(BracketRequest request)
        {
            string entryId = SubmitOrder(request.Entry);
            if (entryId == null)
            {
                return null;
            }

            int quantity = _orderBook[entryId].Quantity;
            BracketHandle handle = new() { EntryOrderId = entryId };
            _pendingBrackets[entryId] = (request.StopPrice, request.TargetPrice, quantity, handle);
            return handle;
        }

        /// <summary>
        /// Matches all working orders against the current bar, applies slippage and commission, and returns the resulting trades.
        /// Filled orders are removed from the book; unfilled orders remain working (GTC).
        /// Records the bar timestamp so subsequent <see cref="SubmitOrder"/> calls can stamp orders with simulation time.
        /// </summary>
        public IEnumerable<Trade> ProcessBar(MarketSlice slice)
        {
            _currentBarTimestamp = slice.Timestamp;

            List<Order> snapshot = _orderBook.Values.ToList();
            List<Trade> trades = new();
            foreach (IGrouping<string, Order> symbolGroup in snapshot.GroupBy(o => o.Symbol))
            {
                string symbol = symbolGroup.Key;
                if (!slice.HasBar(symbol))
                {
                    continue;
                }

                Candle candle = slice.BarsBySymbol[symbol];
                IEnumerable<FillResult> fills = _fillModel.DetermineFills(symbolGroup, candle);
                foreach (FillResult fill in fills)
                {
                    if (!_orderBook.ContainsKey(fill.OrderId))
                    {
                        continue;
                    }

                    Order filledOrder = _orderBook[fill.OrderId];
                    _orderBook.Remove(fill.OrderId);
                    BracketLeg leg = _legRoles.TryGetValue(fill.OrderId, out BracketLeg filledLeg) ? filledLeg : BracketLeg.None;
                    _legRoles.Remove(fill.OrderId);

                    if (_ocoLinks.TryGetValue(fill.OrderId, out string siblingId))
                    {
                        _ocoLinks.Remove(fill.OrderId);
                        _ocoLinks.Remove(siblingId);
                        _orderBook.Remove(siblingId);
                        _legRoles.Remove(siblingId);
                    }

                    decimal rawPrice = fill.Price;
                    decimal adjustedPrice = _slippageModel?.Apply(rawPrice, filledOrder.Side) ?? rawPrice;
                    decimal slippageAmount = Math.Abs(adjustedPrice - rawPrice);
                    decimal commission = _commissionModel?.Calculate(adjustedPrice * fill.Quantity, fill.Quantity) ?? 0m;

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
                        Timestamp = slice.Timestamp,
                        Leg = leg
                    };
                    _portfolio.ApplyTrade(trade);
                    trades.Add(trade);

                    if (_pendingBrackets.TryGetValue(fill.OrderId, out (decimal stopPrice, decimal targetPrice, int quantity, BracketHandle handle) bracket))
                    {
                        _pendingBrackets.Remove(fill.OrderId);
                        // Protective legs close the entry, so they take the side opposite the entry:
                        // a long entry arms Sell legs, a short entry arms Buy legs.
                        OrderSide legSide = filledOrder.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                        string stopId = ArmBracketLeg(symbol, legSide, OrderType.Stop, bracket.stopPrice, bracket.quantity, BracketLeg.StopLoss);
                        string targetId = ArmBracketLeg(symbol, legSide, OrderType.Limit, bracket.targetPrice, bracket.quantity, BracketLeg.TakeProfit);
                        _ocoLinks[stopId] = targetId;
                        _ocoLinks[targetId] = stopId;
                        bracket.handle.StopOrderId = stopId;
                        bracket.handle.TargetOrderId = targetId;
                    }
                }
            }

            return trades;
        }

        private string ArmBracketLeg(string symbol, OrderSide side, OrderType type, decimal price, int quantity, BracketLeg leg)
        {
            Order order = new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = side,
                Type = type,
                Price = price,
                Quantity = quantity,
                SubmittedAt = _currentBarTimestamp
            };
            _orderBook[order.Id] = order;
            _legRoles[order.Id] = leg;
            RecordLevelChange(symbol, leg, price, order.Id);

            return order.Id;
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
            _legRoles.Remove(orderId);
        }


        /// <summary>
        /// Updates the trigger price of a working order. No-ops if the order has already filled or is unknown.
        /// </summary>
        public void Modify(string orderId, decimal newPrice)
        {
            if (_orderBook.TryGetValue(orderId, out Order order))
            {
                order.Price = newPrice;
                // Record the moved level only for a known protective leg (a trailed stop or a moved
                // target); a plain working order carries no leg role and is not part of the ledger.
                if (_legRoles.TryGetValue(orderId, out BracketLeg leg))
                {
                    RecordLevelChange(order.Symbol, leg, newPrice, orderId);
                }
            }
        }
    }
}
