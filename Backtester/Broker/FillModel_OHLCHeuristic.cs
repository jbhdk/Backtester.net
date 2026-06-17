using System;
using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Fill model that uses OHLC bar prices to determine whether limit and stop orders trigger,
    /// and fills market orders at the bar's open price.
    /// </summary>
    public class FillModel_OHLCHeuristic : IFillModel
    {
        /// <summary>
        /// Evaluates each order against the bar's OHLC data and yields a fill for every order that triggers.
        /// </summary>
        public IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar)
        {
            foreach (Order order in orders)
            {
                FillResult fill = TryFill(order, bar);
                if (fill != null)
                {
                    yield return fill;
                }

            }
        }

        private static FillResult TryFill(Order order, Candle bar)
        {
            return (order.Type, order.Side) switch
            {
                (OrderType.Market, _) => Fill(order, bar.Open),
                (OrderType.Limit, OrderSide.Buy) when bar.Low <= order.Price => Fill(order, order.Price!.Value),
                (OrderType.Limit, OrderSide.Sell) when bar.High >= order.Price => Fill(order, order.Price!.Value),
                (OrderType.Stop, OrderSide.Buy) when bar.High >= order.Price => Fill(order, order.Price!.Value),
                (OrderType.Stop, OrderSide.Sell) when bar.Low <= order.Price => Fill(order, order.Price!.Value),
                _ => null
            };
        }


        private static FillResult Fill(Order order, decimal price)
        {
            return new()
            {
                OrderId = order.Id,
                TradeId = Guid.NewGuid().ToString(),
                Price = price,
                Quantity = order.Quantity
            };
        }

    }
}
