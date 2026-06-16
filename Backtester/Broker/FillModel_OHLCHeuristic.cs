using System;
using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;

    public class FillModel_OHLCHeuristic : IFillModel
    {
        public IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar)
        {
            foreach (var order in orders)
            {
                var fill = TryFill(order, bar);
                if (fill != null) yield return fill;
            }
        }

        private static FillResult TryFill(Order order, Candle bar) => (order.Type, order.Side) switch
        {
            (OrderType.Market, _)                                                    => Fill(order, bar.Open),
            (OrderType.Limit,  OrderSide.Buy)  when bar.Low  <= order.Price         => Fill(order, order.Price!.Value),
            (OrderType.Limit,  OrderSide.Sell) when bar.High >= order.Price         => Fill(order, order.Price!.Value),
            (OrderType.Stop,   OrderSide.Buy)  when bar.High >= order.Price         => Fill(order, order.Price!.Value),
            (OrderType.Stop,   OrderSide.Sell) when bar.Low  <= order.Price         => Fill(order, order.Price!.Value),
            _                                                                        => null
        };

        private static FillResult Fill(Order order, decimal price) => new()
        {
            OrderId  = order.Id,
            TradeId  = Guid.NewGuid().ToString(),
            Price    = price,
            Quantity = order.Quantity
        };
    }
}
