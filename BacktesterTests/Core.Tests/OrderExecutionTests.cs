using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class OrderExecutionTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Order MakeOrder(OrderType type, OrderSide side, decimal? price = null, int qty = 10) => new()
        {
            Id = "order-1",
            Symbol = "AAPL",
            Type = type,
            Side = side,
            Price = price,
            Quantity = qty,
            SubmittedAt = T0
        };

        private static Candle Bar(decimal open, decimal high, decimal low, decimal close) => new()
        {
            Timestamp = T0,
            Open = open, High = high, Low = low, Close = close, Volume = 1000
        };

        private static IReadOnlyList<FillResult> Fill(Order order, Candle bar) =>
            new FillModel_OHLCHeuristic().DetermineFills(new[] { order }, bar).ToList();

        [Fact]
        public void Market_FillsAtBarOpen()
        {
            Order order = MakeOrder(OrderType.Market, OrderSide.Buy);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(100m, fills[0].Price);
            Assert.Equal(10, fills[0].Quantity);
        }

        [Fact]
        public void LimitBuy_FillsAtLimitPrice_WhenBarLowAtOrBelowLimit()
        {
            // limit=95, bar.Low=90 → 90 ≤ 95, should fill
            Order order = MakeOrder(OrderType.Limit, OrderSide.Buy, price: 95m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(95m, fills[0].Price);
        }

        [Fact]
        public void LimitBuy_NoFill_WhenBarLowAboveLimit()
        {
            // limit=85, bar.Low=90 → 90 > 85, no fill
            Order order = MakeOrder(OrderType.Limit, OrderSide.Buy, price: 85m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Empty(fills);
        }

        [Fact]
        public void LimitSell_FillsAtLimitPrice_WhenBarHighAtOrAboveLimit()
        {
            // limit=105, bar.High=110 → 110 ≥ 105, should fill
            Order order = MakeOrder(OrderType.Limit, OrderSide.Sell, price: 105m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(105m, fills[0].Price);
        }

        [Fact]
        public void LimitSell_NoFill_WhenBarHighBelowLimit()
        {
            // limit=115, bar.High=110 → 110 < 115, no fill
            Order order = MakeOrder(OrderType.Limit, OrderSide.Sell, price: 115m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Empty(fills);
        }

        [Fact]
        public void StopBuy_FillsAtStopPrice_WhenBarHighAtOrAboveStop()
        {
            // stop=105, bar.High=110 → 110 ≥ 105, should fill
            Order order = MakeOrder(OrderType.Stop, OrderSide.Buy, price: 105m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(105m, fills[0].Price);
        }

        [Fact]
        public void StopBuy_NoFill_WhenBarHighBelowStop()
        {
            // stop=115, bar.High=110 → 110 < 115, no fill
            Order order = MakeOrder(OrderType.Stop, OrderSide.Buy, price: 115m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Empty(fills);
        }

        [Fact]
        public void StopSell_FillsAtStopPrice_WhenBarLowAtOrBelowStop()
        {
            // stop=95, bar.Low=90 → 90 ≤ 95, should fill
            Order order = MakeOrder(OrderType.Stop, OrderSide.Sell, price: 95m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(95m, fills[0].Price);
        }

        [Fact]
        public void StopSell_NoFill_WhenBarLowAboveStop()
        {
            // stop=85, bar.Low=90 → 90 > 85, no fill
            Order order = MakeOrder(OrderType.Stop, OrderSide.Sell, price: 85m);
            Candle bar = Bar(open: 100m, high: 110m, low: 90m, close: 105m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Empty(fills);
        }
    }
}
