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

        private static Order MakeOrder(OrderType type, OrderSide side, decimal? price = null, int qty = 10)
        {
            return new()
            {
                Id = "order-1",
                Symbol = "AAPL",
                Type = type,
                Side = side,
                Price = price,
                Quantity = qty,
                SubmittedAt = T0
            };
        }

        private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        {
            return new()
            {
                Timestamp = T0,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = 1000
            };
        }

        private static IReadOnlyList<FillResult> Fill(Order order, Candle bar)
        {
            return new FillModel_OHLCHeuristic().DetermineFills(new[] { order }, bar).ToList();
        }


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
        public void LimitBuy_FillsAtBarOpen_WhenBarGapsBelowLimit()
        {
            // limit=95, bar gaps down and opens at 90 (below the limit) → a resting bid fills at the
            // better gapped open, not the limit price. Gap-aware pricing credits the improvement.
            Order order = MakeOrder(OrderType.Limit, OrderSide.Buy, price: 95m);
            Candle bar = Bar(open: 90m, high: 93m, low: 88m, close: 91m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(90m, fills[0].Price);
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
        public void LimitSell_FillsAtBarOpen_WhenBarGapsAboveLimit()
        {
            // limit=105, bar gaps up and opens at 112 (above the limit) → a resting offer fills at
            // the better gapped open, not the limit price. Gap-aware pricing credits the improvement.
            Order order = MakeOrder(OrderType.Limit, OrderSide.Sell, price: 105m);
            Candle bar = Bar(open: 112m, high: 115m, low: 110m, close: 113m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(112m, fills[0].Price);
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
        public void StopBuy_FillsAtBarOpen_WhenBarGapsAboveStop()
        {
            // stop=105, bar gaps up and opens at 112 (above the stop) → marketable, fills at the
            // gapped open, not the stop price. A short's protective buy-stop takes the real gap loss.
            Order order = MakeOrder(OrderType.Stop, OrderSide.Buy, price: 105m);
            Candle bar = Bar(open: 112m, high: 115m, low: 110m, close: 113m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(112m, fills[0].Price);
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
        public void StopSell_FillsAtBarOpen_WhenBarGapsBelowStop()
        {
            // stop=95, bar gaps down and opens at 90 (below the stop) → marketable, fills at the
            // gapped open, not the stop price. A protective long stop takes the real gap loss.
            Order order = MakeOrder(OrderType.Stop, OrderSide.Sell, price: 95m);
            Candle bar = Bar(open: 90m, high: 92m, low: 88m, close: 89m);

            IReadOnlyList<FillResult> fills = Fill(order, bar);

            Assert.Single(fills);
            Assert.Equal(90m, fills[0].Price);
        }

        [Fact]
        public void StopSell_FillsAtStopPrice_WhenBarOpensExactlyAtStop()
        {
            // stop=95, bar opens exactly at 95 → the min(stop, open) pivot: no gap, fills at the
            // stop price. Guards the equality edge between "trades through" and "gapped past".
            Order order = MakeOrder(OrderType.Stop, OrderSide.Sell, price: 95m);
            Candle bar = Bar(open: 95m, high: 96m, low: 88m, close: 90m);

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
