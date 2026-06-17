using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Backtester.Strategies;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    public class StrategyTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close) => new()
        {
            Timestamp = ts,
            Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000
        };

        private static PortfolioSnapshot EmptySnapshot() => new()
        {
            Timestamp = T0,
            Cash = 10_000m,
            Equity = 10_000m,
            Positions = new List<Position>()
        };

        private static PortfolioSnapshot SnapshotWithPosition(string symbol) => new()
        {
            Timestamp = T0,
            Cash = 9_000m,
            Equity = 10_000m,
            Positions = new List<Position> { new() { Symbol = symbol, Quantity = 10, AveragePrice = 100m } }
        };

        private static IReadOnlyList<OrderRequest> RunBars(
            MovingAverageCrossStrategy strategy, string symbol, params decimal[] closes)
        {
            List<OrderRequest> results = new List<OrderRequest>();
            for (int i = 0; i < closes.Length; i++)
            {
                PortfolioSnapshot snapshot = EmptySnapshot();
                IEnumerable<OrderRequest> orders = strategy.OnBar(symbol, Bar(T0.AddDays(i), closes[i]), snapshot);
                results.AddRange(orders);
            }
            return results;
        }

        [Fact]
        public void NoOrder_WhenFewerThanSlowPeriodBars()
        {
            // fast=3, slow=5 — feed only 4 bars (one short of slow period)
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);

            IReadOnlyList<OrderRequest> orders = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m);

            Assert.Empty(orders);
        }

        [Fact]
        public void NoOrder_OnFirstBarWithBothSMAsCalculable()
        {
            // 5th bar is the first with a slow MA — no prior direction to compare, so no order
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);

            IReadOnlyList<OrderRequest> orders = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m, 60m);

            Assert.Empty(orders);
        }

        [Fact]
        public void NoOrder_WhenFastContinuesAboveSlow_NoCross()
        {
            // Steadily rising prices: fast always above slow after warmup, never crosses
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);

            // bar 5 (price=5): fast=4 slow=3 → direction=true (first time, no order)
            // bar 6 (price=6): fast=5 slow=4 → still true → no cross
            IReadOnlyList<OrderRequest> orders = RunBars(strategy, "AAPL", 1m, 2m, 3m, 4m, 5m, 6m);

            Assert.Empty(orders);
        }

        [Fact]
        public void EmitsBuy_OnGoldenCross()
        {
            // Prices fall then recover, producing a fast-crosses-above-slow event on bar 8 (index 7)
            // [100,90,80,70,60,70,80,90]: on bar 8 fastMA=80 > slowMA=74 (was below on bar 7)
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);

            IReadOnlyList<OrderRequest> orders = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m, 60m, 70m, 80m, 90m);

            Assert.Single(orders);
            Assert.Equal(OrderSide.Buy, orders[0].Side);
            Assert.Equal(OrderType.Market, orders[0].Type);
            Assert.Equal("AAPL", orders[0].Symbol);
        }

        [Fact]
        public void EmitsSell_OnDeathCross_WhenPositionExists()
        {
            // Rising then falling: fast crosses below slow → death cross on bar 8 (index 7)
            // [60,70,80,90,100,90,80,70]: on bar 8 fastMA=80 < slowMA=86 (was above on bar 7)
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);
            List<OrderRequest> results = new List<OrderRequest>();
            decimal[] prices = new[] { 60m, 70m, 80m, 90m, 100m, 90m, 80m, 70m };

            for (int i = 0; i < prices.Length; i++)
            {
                // Provide a position in the snapshot so the sell guard passes
                PortfolioSnapshot snapshot = i >= 6 ? SnapshotWithPosition("AAPL") : EmptySnapshot();
                IEnumerable<OrderRequest> orders = strategy.OnBar("AAPL", Bar(T0.AddDays(i), prices[i]), snapshot);
                results.AddRange(orders);
            }

            Assert.Single(results);
            Assert.Equal(OrderSide.Sell, results[0].Side);
        }

        [Fact]
        public void NoSell_OnDeathCross_WhenNoPositionExists()
        {
            // Same death-cross series but snapshot always has no position → sell is suppressed
            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);

            IReadOnlyList<OrderRequest> orders = RunBars(strategy, "AAPL", 60m, 70m, 80m, 90m, 100m, 90m, 80m, 70m);

            Assert.Empty(orders);
        }
    }
}
