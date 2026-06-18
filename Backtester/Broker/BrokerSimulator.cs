using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Backtester.Models.Commission;
using Backtester.Models.Risk;
using Backtester.Models.Sizing;
using Backtester.Models.Slippage;

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
        private readonly IRiskModel _riskModel;
        // key: order ID → working order (GTC until filled or cancelled)
        private readonly Dictionary<string, Order> _orderBook = new();
        // key: entry order ID → (stopPrice, targetPrice, quantity, handle) for pending bracket legs
        private readonly Dictionary<string, (decimal stopPrice, decimal targetPrice, int quantity, BracketHandle handle)> _pendingBrackets = new();
        // key: order ID → sibling order ID for OCO pairs (stop ↔ target)
        private readonly Dictionary<string, string> _ocoLinks = new();
        private DateTime _currentBarTimestamp;

        /// <summary>
        /// Initializes a new broker simulator. All model parameters are optional; defaults are applied when null.
        /// </summary>
        public BrokerSimulator(
            Portfolio portfolio,
            IFillModel fillModel = null,
            ICommissionModel commissionModel = null,
            ISlippageModel slippageModel = null,
            ISizingModel sizingModel = null,
            IRiskModel riskModel = null)
        {
            _portfolio = portfolio;
            _fillModel = fillModel ?? new FillModel_OHLCHeuristic();
            _commissionModel = commissionModel;
            _slippageModel = slippageModel;
            _sizingModel = sizingModel;
            _riskModel = riskModel;
        }

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

            if (_riskModel != null && !_riskModel.Accept(request, _portfolio))
            {

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

                    if (_ocoLinks.TryGetValue(fill.OrderId, out string siblingId))
                    {
                        _ocoLinks.Remove(fill.OrderId);
                        _ocoLinks.Remove(siblingId);
                        _orderBook.Remove(siblingId);
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
                        Timestamp = slice.Timestamp
                    };
                    _portfolio.ApplyTrade(trade);
                    trades.Add(trade);

                    if (_pendingBrackets.TryGetValue(fill.OrderId, out (decimal stopPrice, decimal targetPrice, int quantity, BracketHandle handle) bracket))
                    {
                        _pendingBrackets.Remove(fill.OrderId);
                        string stopId = ArmBracketLeg(symbol, OrderType.Stop, bracket.stopPrice, bracket.quantity);
                        string targetId = ArmBracketLeg(symbol, OrderType.Limit, bracket.targetPrice, bracket.quantity);
                        _ocoLinks[stopId] = targetId;
                        _ocoLinks[targetId] = stopId;
                        bracket.handle.StopOrderId = stopId;
                        bracket.handle.TargetOrderId = targetId;
                    }
                }
            }
            return trades;
        }

        private string ArmBracketLeg(string symbol, OrderType type, decimal price, int quantity)
        {
            Order order = new()

            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = OrderSide.Sell,
                Type = type,
                Price = price,
                Quantity = quantity,
                SubmittedAt = _currentBarTimestamp
            };
            _orderBook[order.Id] = order;
            return order.Id;
        }

        /// <summary>
        /// Removes a working order from the book so it will never fill. No-ops if the order has already filled or is unknown.
        /// </summary>
        public void Cancel(string orderId)
        {
            _orderBook.Remove(orderId);
        }


        /// <summary>
        /// Updates the trigger price of a working order. No-ops if the order has already filled or is unknown.
        /// </summary>
        public void Modify(string orderId, decimal newPrice)
        {
            if (_orderBook.TryGetValue(orderId, out Order order))
            {

                order.Price = newPrice;
            }

        }
    }
}
