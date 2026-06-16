using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Broker
{
    using Backtester.Core;
    using Backtester.Data;
    using Backtester.Models.Commission;
    using Backtester.Models.Risk;
    using Backtester.Models.Sizing;
    using Backtester.Models.Slippage;

    public class BrokerSimulator : IBrokerSimulator
    {
        private readonly Portfolio _portfolio;
        private readonly IFillModel _fillModel;
        private readonly ICommissionModel _commissionModel;
        private readonly ISlippageModel _slippageModel;
        private readonly ISizingModel _sizingModel;
        private readonly IRiskModel _riskModel;
        private readonly Queue<Order> _pendingOrders = new();

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

        public string SubmitOrder(OrderRequest request)
        {
            if (_sizingModel != null)
            {
                var sized = _sizingModel.Size(request, _portfolio);
                if (sized == 0) return null;
                request.Quantity = sized;
            }

            if (_riskModel != null && !_riskModel.Accept(request, _portfolio))
                return null;

            var order = new Order
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

        public IEnumerable<Trade> ProcessBar(MarketSlice slice)
        {
            var orders = new List<Order>();
            while (_pendingOrders.TryDequeue(out var order))
                orders.Add(order);

            var trades = new List<Trade>();
            foreach (var symbolGroup in orders.GroupBy(o => o.Symbol))
            {
                var symbol = symbolGroup.Key;
                if (!slice.HasBar(symbol))
                    continue;

                var candle = slice.BarsBySymbol[symbol];
                var fills = _fillModel.DetermineFills(symbolGroup, candle);
                foreach (var fill in fills)
                {
                    var order = symbolGroup.First(o => o.Id == fill.OrderId);

                    var rawPrice = fill.Price;
                    var adjustedPrice = _slippageModel?.Apply(rawPrice, order.Side) ?? rawPrice;
                    var slippageAmount = Math.Abs(adjustedPrice - rawPrice);
                    var commission = _commissionModel?.Calculate(adjustedPrice * fill.Quantity, fill.Quantity) ?? 0m;

                    var trade = new Trade
                    {
                        Id = fill.TradeId,
                        OrderId = fill.OrderId,
                        Symbol = symbol,
                        Side = order.Side,
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
