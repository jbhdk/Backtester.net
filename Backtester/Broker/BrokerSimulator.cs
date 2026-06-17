using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Backtester.Data;
using Backtester.Models.Commission;
using Backtester.Models.Risk;
using Backtester.Models.Sizing;
using Backtester.Models.Slippage;

namespace Backtester.Broker
{
    /// <summary>
    /// Simulates order execution against historical bar data, applying sizing, risk, slippage, and commission models.
    /// </summary>
    public class BrokerSimulator : IBrokerSimulator
    {
        private readonly Portfolio _portfolio;
        private readonly IFillModel _fillModel;
        private readonly ICommissionModel _commissionModel;
        private readonly ISlippageModel _slippageModel;
        private readonly ISizingModel _sizingModel;
        private readonly IRiskModel _riskModel;
        private readonly Queue<Order> _pendingOrders = new();

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
                if (sized == 0) return null;
                request.Quantity = sized;
            }

            if (_riskModel != null && !_riskModel.Accept(request, _portfolio))
                return null;

            Order order = new Order
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Price = request.Price,
                Quantity = request.Quantity,
                SubmittedAt = DateTime.UtcNow
            };
            _pendingOrders.Enqueue(order);
            return order.Id;
        }

        /// <summary>
        /// Matches all pending orders against the current bar, applies slippage and commission, and returns the resulting trades.
        /// </summary>
        public IEnumerable<Trade> ProcessBar(MarketSlice slice)
        {
            List<Order> orders = new();
            while (_pendingOrders.TryDequeue(out Order order))
                orders.Add(order);

            List<Trade> trades = new();
            foreach (IGrouping<string, Order> symbolGroup in orders.GroupBy(o => o.Symbol))
            {
                string symbol = symbolGroup.Key;
                if (!slice.HasBar(symbol))
                    continue;

                Candle candle = slice.BarsBySymbol[symbol];
                IEnumerable<FillResult> fills = _fillModel.DetermineFills(symbolGroup, candle);
                foreach (FillResult fill in fills)
                {
                    Order filledOrder = symbolGroup.First(o => o.Id == fill.OrderId);

                    decimal rawPrice = fill.Price;
                    decimal adjustedPrice = _slippageModel?.Apply(rawPrice, filledOrder.Side) ?? rawPrice;
                    decimal slippageAmount = Math.Abs(adjustedPrice - rawPrice);
                    decimal commission = _commissionModel?.Calculate(adjustedPrice * fill.Quantity, fill.Quantity) ?? 0m;

                    Trade trade = new Trade
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
                }
            }
            return trades;
        }
    }
}
