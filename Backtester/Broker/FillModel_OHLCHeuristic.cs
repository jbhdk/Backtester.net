using System;
using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Fill model that uses OHLC bar prices to determine whether limit and stop orders trigger, and
    /// prices every fill gap-aware: no better than the bar's open. A Market fills at the open; a
    /// triggered Stop or Limit fills at its trigger unless the bar gapped past it, in which case the
    /// open. So a below-market trigger (Buy limit, Sell stop) fills at min(trigger, open) and an
    /// above-market trigger (Sell limit, Buy stop) at max(trigger, open).
    /// </summary>
    public class FillModel_OHLCHeuristic : IFillModel
    {
        /// <summary>
        /// Evaluates each order against the bar's OHLC data and yields a gap-aware fill for every order that triggers.
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
                (OrderType.Limit, OrderSide.Buy) when bar.Low <= order.Price => Fill(order, Math.Min(order.Price!.Value, bar.Open)),
                (OrderType.Limit, OrderSide.Sell) when bar.High >= order.Price => Fill(order, Math.Max(order.Price!.Value, bar.Open)),
                (OrderType.Stop, OrderSide.Buy) when bar.High >= order.Price => Fill(order, Math.Max(order.Price!.Value, bar.Open)),
                (OrderType.Stop, OrderSide.Sell) when bar.Low <= order.Price => Fill(order, Math.Min(order.Price!.Value, bar.Open)),
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
